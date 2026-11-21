using System.Security.Cryptography;
using DocControl.Api.Data;
using DocControl.Api.Models;
using DocControl.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Controllers;

[ApiController]
public class ShareLinksController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FolderPermissionService _folderPermissionService;
    private readonly string _contentRootPath;
    private readonly string _storagePath;

    public ShareLinksController(AppDbContext context, IWebHostEnvironment env, FolderPermissionService folderPermissionService)
    {
        _context = context;
        _folderPermissionService = folderPermissionService;
        _contentRootPath = Path.GetFullPath(env.ContentRootPath);
        _storagePath = Path.GetFullPath(Path.Combine(_contentRootPath, "Storage"));
    }

    // ═══════════════════════════════════════════════════════════
    //  GESTIÓN INTERNA DE ENLACES COMPARTIDOS (RF-20)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Listar todos los enlaces compartidos de un documento ordenados por FechaCreacion descendente.</summary>
    [HttpGet("api/documents/{id:int}/share-links")]
    public async Task<IActionResult> ObtenerEnlacesDocumento(int id)
    {
        if (!await _context.Documentos.AnyAsync(d => d.Id == id))
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"El documento con Id {id} no existe." });

        var enlaces = await _context.EnlacesCompartidos
            .AsNoTracking()
            .Where(l => l.DocumentoId == id)
            .OrderByDescending(l => l.FechaCreacion)
            .ThenByDescending(l => l.Id)
            .ToListAsync();

        var ahora = DateTime.UtcNow;
        var resultado = enlaces.Select(l => MapearEnlaceResumen(l, ahora)).ToList();
        return Ok(resultado);
    }

    /// <summary>Generar un nuevo enlace temporal de solo lectura (Administrador, Revisor o Editor).</summary>
    [HttpPost("api/documents/{id:int}/share-links")]
    public async Task<IActionResult> CrearEnlaceCompartido(int id, [FromBody] SolicitudCrearEnlaceCompartido solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido." });

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = "El usuario es obligatorio." });

        var doc = await _context.Documentos.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"El documento con Id {id} no existe." });

        if (doc.Eliminado)
            return BadRequest(new RespuestaError { Error = "Documento en papelera", Detalle = "No se pueden generar enlaces de un documento en la papelera." });

        string nivelCarpeta = await _folderPermissionService.ObtenerNivelAccesoEfectivoAsync(doc.CarpetaId, doc.Departamento, rol.ToString());
        bool permitidoEnlace = FolderPermissionService.CumpleNivelMinimo(nivelCarpeta, FolderPermissionService.Edicion) ||
            (rol == RolUsuario.Revisor && nivelCarpeta != FolderPermissionService.SinAcceso);
        if (!permitidoEnlace)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, $"crear enlaces compartidos temporales en la carpeta '{doc.Departamento}' (nivel actual: {nivelCarpeta})");

        var ahora = DateTime.UtcNow;
        DateTime fechaExpiracion;

        if (!string.IsNullOrWhiteSpace(solicitud.FechaExpiracionPersonalizada))
        {
            if (!DateTime.TryParse(solicitud.FechaExpiracionPersonalizada, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var fechaParseada))
            {
                return BadRequest(new RespuestaError { Error = "Fecha inválida", Detalle = "El formato de la fecha de expiración personalizada no es válido." });
            }

            fechaExpiracion = fechaParseada.Kind == DateTimeKind.Utc ? fechaParseada : fechaParseada.ToUniversalTime();
            if (fechaExpiracion <= ahora.AddMinutes(1))
                return BadRequest(new RespuestaError { Error = "Fecha inválida", Detalle = "La fecha de expiración personalizada debe ser futura." });
        }
        else
        {
            int horas = solicitud.DuracionHoras.GetValueOrDefault(24);
            if (horas <= 0 || horas > 24 * 365)
                horas = 24;
            fechaExpiracion = ahora.AddHours(horas);
        }

        string token = GenerarTokenSeguro();
        var enlace = new EnlaceCompartido
        {
            Token = token,
            DocumentoId = doc.Id,
            VersionId = null,
            CreadoPor = solicitud.Usuario.Trim(),
            FechaCreacion = ahora,
            FechaExpiracion = fechaExpiracion,
            Revocado = false,
            PermiteDescarga = solicitud.PermiteDescarga,
            TotalAccesos = 0
        };

        _context.EnlacesCompartidos.Add(enlace);
        RegistrarEnBitacora(solicitud.Usuario.Trim(), rol, doc.Codigo, doc.Nombre, doc.VersionActual,
            AccionBitacora.CREAR_ENLACE_TEMPORAL,
            $"Enlace compartido temporal creado por '{enlace.CreadoPor}'. Expira: {fechaExpiracion:dd/MM/yyyy HH:mm:ss} UTC. Descarga permitida: {(enlace.PermiteDescarga ? "Sí" : "No")}.");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = "Enlace temporal generado exitosamente.",
            enlace = MapearEnlaceResumen(enlace, ahora)
        });
    }

    /// <summary>Revocar inmediatamente un enlace compartido activo (Administrador, Revisor o Editor).</summary>
    [HttpPost("api/share-links/{linkId:int}/revoke")]
    public async Task<IActionResult> RevocarEnlace(int linkId, [FromBody] SolicitudUsuario solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido." });

        var enlace = await _context.EnlacesCompartidos.FirstOrDefaultAsync(l => l.Id == linkId);
        if (enlace == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"El enlace con Id {linkId} no existe." });

        if (rol != RolUsuario.Administrador && rol != RolUsuario.Revisor && rol != RolUsuario.Editor)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, enlace.DocumentoId, "revocar enlaces compartidos temporales");

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = "El usuario es obligatorio." });

        var doc = await _context.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == enlace.DocumentoId);
        var ahora = DateTime.UtcNow;

        if (!enlace.Revocado)
        {
            enlace.Revocado = true;
            enlace.FechaRevocacion = ahora;

            RegistrarEnBitacora(solicitud.Usuario.Trim(), rol,
                doc?.Codigo ?? "N/A", doc?.Nombre ?? "(documento)", doc?.VersionActual ?? "-",
                AccionBitacora.REVOCAR_ENLACE_TEMPORAL,
                $"Enlace temporal (Id #{enlace.Id}) revocado por '{solicitud.Usuario.Trim()}'.");

            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            mensaje = "Enlace temporal revocado correctamente.",
            enlace = MapearEnlaceResumen(enlace, ahora)
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  CONSULTA PÚBLICA EXTERNA POR TOKEN (SIN SESIÓN INTERNA)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Consultar metadatos públicos de un documento compartido mediante token temporal (RF-20).</summary>
    [HttpGet("api/share/{token}")]
    public async Task<IActionResult> ConsultarEnlacePublico(string token)
    {
        var validacion = await ValidarTokenPublicoAsync(token);
        if (validacion.ErrorResult != null)
            return validacion.ErrorResult;

        var enlace = validacion.Enlace!;
        var doc = validacion.Documento!;
        var version = validacion.Version!;

        enlace.TotalAccesos += 1;

        RegistrarEnBitacora("Consulta Externa (Token)", RolUsuario.Lector,
            doc.Codigo, doc.Nombre, version.VersionTexto,
            AccionBitacora.ACCESO_ENLACE_EXTERNO,
            $"Acceso externo de solo lectura mediante enlace compartido (Id #{enlace.Id}, acceso #{enlace.TotalAccesos}).");

        await _context.SaveChangesAsync();

        bool tieneFisico = ResolverRutaArchivoFisico(version.RutaArchivo) != null;

        return Ok(new DocumentoCompartidoPublicoDto
        {
            Codigo = doc.Codigo,
            Nombre = doc.Nombre,
            Tipo = doc.Tipo.ToString(),
            VersionActual = version.VersionTexto,
            TamanoBytes = version.TamanoBytes,
            HashSha256 = version.HashSHA256,
            FechaSubida = version.FechaCreacion,
            FechaExpiracion = enlace.FechaExpiracion,
            PermiteDescarga = enlace.PermiteDescarga,
            TieneArchivoFisico = tieneFisico,
            NombreArchivo = version.NombreArchivo
        });
    }

    /// <summary>Vista previa inline pública mediante token temporal (PDF, Imágenes y Texto).</summary>
    [HttpGet("api/share/{token}/preview")]
    public async Task<IActionResult> VistaPreviaPublica(string token)
    {
        var validacion = await ValidarTokenPublicoAsync(token);
        if (validacion.ErrorResult != null)
            return validacion.ErrorResult;

        var version = validacion.Version!;
        string? rutaCompleta = ResolverRutaArchivoFisico(version.RutaArchivo);
        if (rutaCompleta == null)
        {
            return NotFound(new
            {
                code = "FILE_NOT_AVAILABLE",
                message = "Esta versión de prueba inicial no cuenta con un archivo binario físico en el servidor."
            });
        }

        string ext = Path.GetExtension(rutaCompleta);
        if (string.IsNullOrEmpty(ext))
            ext = Path.GetExtension(version.NombreArchivo);
        string mime = ObtenerMimeType(ext);

        string nombreLimpio = Path.GetFileName(version.NombreArchivo).Replace("\"", string.Empty);
        string nombreAscii = new string(QuitarAcentos(nombreLimpio).Where(c => c >= 32 && c <= 126).ToArray());
        if (string.IsNullOrWhiteSpace(nombreAscii)) nombreAscii = $"documento{ext}";

        Response.Headers.Remove("X-Frame-Options");
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{nombreAscii}\"; filename*=UTF-8''{Uri.EscapeDataString(nombreLimpio)}";
        var stream = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mime, enableRangeProcessing: true);
    }

    /// <summary>Descarga pública del archivo si el enlace tiene PermiteDescarga = true.</summary>
    [HttpGet("api/share/{token}/download")]
    public async Task<IActionResult> DescargarPublico(string token)
    {
        var validacion = await ValidarTokenPublicoAsync(token);
        if (validacion.ErrorResult != null)
            return validacion.ErrorResult;

        var enlace = validacion.Enlace!;
        var doc = validacion.Documento!;
        var version = validacion.Version!;

        if (!enlace.PermiteDescarga)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "DOWNLOAD_FORBIDDEN",
                message = "Este enlace compartido es de solo vista previa y no permite descargar el archivo."
            });
        }

        string? rutaCompleta = ResolverRutaArchivoFisico(version.RutaArchivo);
        if (rutaCompleta == null)
        {
            return NotFound(new
            {
                code = "FILE_NOT_AVAILABLE",
                message = "El archivo físico no se encuentra disponible en el almacenamiento del servidor."
            });
        }

        RegistrarEnBitacora("Consulta Externa (Token)", RolUsuario.Lector,
            doc.Codigo, doc.Nombre, version.VersionTexto,
            AccionBitacora.DESCARGA_ARCHIVO,
            $"Descarga mediante enlace compartido (Id #{enlace.Id}). Archivo: {version.NombreArchivo} ({version.TamanoBytes} bytes).");
        await _context.SaveChangesAsync();

        var stream = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, ObtenerMimeType(Path.GetExtension(rutaCompleta)), version.NombreArchivo, enableRangeProcessing: true);
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS PRIVADOS
    // ═══════════════════════════════════════════════════════════

    private async Task<(EnlaceCompartido? Enlace, Documento? Documento, VersionDocumento? Version, IActionResult? ErrorResult)>
        ValidarTokenPublicoAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, null, null, NotFound(new
            {
                code = "NOT_FOUND",
                message = "El enlace compartido no existe o el documento ya no está disponible."
            }));
        }

        var enlace = await _context.EnlacesCompartidos
            .FirstOrDefaultAsync(l => l.Token == token.Trim());

        if (enlace == null)
        {
            return (null, null, null, NotFound(new
            {
                code = "NOT_FOUND",
                message = "El enlace compartido no existe o el documento ya no está disponible."
            }));
        }

        var doc = await _context.Documentos
            .AsNoTracking()
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == enlace.DocumentoId);

        if (doc == null || doc.Eliminado)
        {
            return (null, null, null, NotFound(new
            {
                code = "NOT_FOUND",
                message = "El enlace compartido no existe o el documento ya no está disponible."
            }));
        }

        if (enlace.Revocado)
        {
            return (null, null, null, StatusCode(StatusCodes.Status410Gone, new
            {
                code = "REVOKED",
                message = "Este enlace fue revocado y ya no permite acceso."
            }));
        }

        if (enlace.FechaExpiracion <= DateTime.UtcNow)
        {
            return (null, null, null, StatusCode(StatusCodes.Status410Gone, new
            {
                code = "EXPIRED",
                message = "Este enlace ha caducado y ya no es válido."
            }));
        }

        VersionDocumento? version = enlace.VersionId.HasValue
            ? doc.HistorialVersiones.FirstOrDefault(v => v.Id == enlace.VersionId.Value)
            : doc.HistorialVersiones.OrderByDescending(v => v.NumeroVersion).FirstOrDefault();

        if (version == null)
        {
            return (null, null, null, NotFound(new
            {
                code = "NOT_FOUND",
                message = "No se encontró una versión válida del documento."
            }));
        }

        return (enlace, doc, version, null);
    }

    private static EnlaceCompartidoResumen MapearEnlaceResumen(EnlaceCompartido l, DateTime ahoraUtc)
    {
        string estado = l.Revocado
            ? "Revocado"
            : (l.FechaExpiracion <= ahoraUtc ? "Expirado" : "Activo");

        return new EnlaceCompartidoResumen
        {
            Id = l.Id,
            DocumentoId = l.DocumentoId,
            Token = l.Token,
            CreadoPor = l.CreadoPor,
            FechaCreacion = l.FechaCreacion,
            FechaExpiracion = l.FechaExpiracion,
            Revocado = l.Revocado,
            FechaRevocacion = l.FechaRevocacion,
            PermiteDescarga = l.PermiteDescarga,
            TotalAccesos = l.TotalAccesos,
            Estado = estado
        };
    }

    private static string GenerarTokenSeguro()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static bool TryParseRol(string? valor, out RolUsuario rol) =>
        Enum.TryParse(valor ?? string.Empty, true, out rol) && Enum.IsDefined(rol);

    private async Task<ObjectResult> DenegarAccesoAsync(string? usuario, RolUsuario rol, int? documentoId, string accionIntentada)
    {
        string codigo = "N/A";
        string nombre = "(sin documento)";
        string version = "-";

        if (documentoId.HasValue)
        {
            var info = await _context.Documentos.AsNoTracking()
                .Where(d => d.Id == documentoId.Value)
                .Select(d => new { d.Codigo, d.Nombre, d.VersionActual })
                .FirstOrDefaultAsync();

            if (info != null)
            {
                codigo = info.Codigo;
                nombre = info.Nombre;
                version = info.VersionActual;
            }
        }

        string actor = string.IsNullOrWhiteSpace(usuario) ? "(sin usuario)" : usuario.Trim();
        RegistrarEnBitacora(actor, rol, codigo, nombre, version, AccionBitacora.ACCESO_DENEGADO,
            $"Intento sin permiso de {accionIntentada}. Rol actual: {rol}.");
        await _context.SaveChangesAsync();

        return StatusCode(StatusCodes.Status403Forbidden, new RespuestaError
        {
            Error = "Acceso denegado",
            Detalle = $"El rol '{rol}' no tiene permisos para {accionIntentada}."
        });
    }

    private void RegistrarEnBitacora(string usuario, RolUsuario rol, string codigo, string nombre, string version,
        AccionBitacora accion, string observaciones)
    {
        string ip = HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        string ua = HttpContext?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        if (ua.Length > 350) ua = ua[..350];

        _context.Bitacora.Add(new RegistroBitacora
        {
            FechaHora = DateTime.UtcNow,
            Usuario = usuario,
            Rol = rol,
            CodigoDocumento = codigo,
            NombreDocumento = nombre,
            Version = version,
            Accion = accion,
            Observaciones = observaciones,
            DatosMaquina = string.IsNullOrEmpty(ua) ? $"IP: {ip}" : $"IP: {ip} | UA: {ua}"
        });
    }

    private string? ResolverRutaSegura(string nombreAlmacenado)
    {
        string ruta = Path.GetFullPath(Path.Combine(_storagePath, nombreAlmacenado));
        return ruta.StartsWith(_storagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? ruta
            : null;
    }

    private string? ResolverRutaArchivoFisico(string? rutaArchivo)
    {
        if (string.IsNullOrWhiteSpace(rutaArchivo)) return null;

        string? enStorage = ResolverRutaSegura(rutaArchivo);
        if (enStorage != null && System.IO.File.Exists(enStorage))
            return enStorage;

        if (System.IO.File.Exists(rutaArchivo))
            return Path.GetFullPath(rutaArchivo);

        string relativaRoot = Path.GetFullPath(Path.Combine(_contentRootPath, rutaArchivo));
        if (relativaRoot.StartsWith(_contentRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(relativaRoot))
            return relativaRoot;

        string soloNombre = Path.GetFileName(rutaArchivo);
        if (!string.IsNullOrWhiteSpace(soloNombre))
        {
            string? fallbackStorage = ResolverRutaSegura(soloNombre);
            if (fallbackStorage != null && System.IO.File.Exists(fallbackStorage))
                return fallbackStorage;
        }

        return null;
    }

    private static string ObtenerMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };

    private static string QuitarAcentos(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        var norm = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
