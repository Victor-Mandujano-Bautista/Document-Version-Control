using System.Security.Cryptography;
using System.Text;
using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private const int MinutosBloqueo = 30;
    private const int MinutosExtension = 15;
    private const long MaxTamanoArchivo = 50L * 1024 * 1024; // 50 MB

    private readonly AppDbContext _context;
    private readonly string _storagePath;

    public DocumentsController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        // Carpeta física "Storage" en la raíz del backend (junto a Program.cs)
        _storagePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "Storage"));
    }

    // ═══════════════════════════════════════════════════════════
    //  CONSULTAS
    // ═══════════════════════════════════════════════════════════

    /// <summary>Obtener todos los documentos con su estado actual.</summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerDocumentos()
    {
        await LiberarBloqueosExpiradosAsync();

        var docs = await _context.Documentos
            .AsNoTracking()
            .Include(d => d.Bloqueo)
            .OrderBy(d => d.Id)
            .ToListAsync();

        return Ok(docs.Select(MapearAResumen).ToList());
    }

    /// <summary>Obtener KPIs de resumen del workspace.</summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> ObtenerKpis()
    {
        await LiberarBloqueosExpiradosAsync();

        var total = await _context.Documentos.CountAsync();
        var bloqueados = await _context.Documentos.CountAsync(d => d.Estado == EstadoDocumento.Bloqueado);

        return Ok(new ResumenKpis
        {
            TotalDocumentos = total,
            DocumentosDisponibles = total - bloqueados,
            DocumentosBloqueados = bloqueados,
            RegistrosBitacora = await _context.Bitacora.CountAsync()
        });
    }

    /// <summary>Obtener historial de versiones de un documento.</summary>
    [HttpGet("{id}/historial")]
    public async Task<IActionResult> ObtenerHistorial(int id)
    {
        if (!await _context.Documentos.AnyAsync(d => d.Id == id))
            return NoEncontrado(id);

        var historial = await _context.Versiones
            .AsNoTracking()
            .Where(v => v.DocumentoId == id)
            .OrderByDescending(v => v.NumeroVersion)
            .ToListAsync();

        return Ok(historial);
    }

    /// <summary>Obtener la bitácora digital de auditoría.</summary>
    [HttpGet("bitacora")]
    public async Task<IActionResult> ObtenerBitacora()
    {
        var bitacora = await _context.Bitacora
            .AsNoTracking()
            .OrderByDescending(b => b.FechaHora)
            .ThenByDescending(b => b.Folio)
            .ToListAsync();

        return Ok(bitacora);
    }

    // ═══════════════════════════════════════════════════════════
    //  BLOQUEO (CHECK-OUT)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Bloquear un documento para edición exclusiva (Check-out).</summary>
    [HttpPost("{id}/lock")]
    public async Task<IActionResult> BloquearDocumento(int id, [FromBody] SolicitudBloqueo solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return RolInvalido(solicitud.Rol, true);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return Denegado($"El rol '{rol}' no tiene permisos para bloquear documentos.");

        await LiberarBloqueosExpiradosAsync();

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);

        if (doc.Estado == EstadoDocumento.Bloqueado && doc.Bloqueo != null && !doc.Bloqueo.EstaExpirado)
            return Conflicto(doc);

        if (doc.EstadoRevision == EstadoRevision.EnRevision)
            return Denegado("El documento está en revisión y no puede bloquearse hasta que se emita el dictamen.");

        var ahora = DateTime.UtcNow;
        doc.Estado = EstadoDocumento.Bloqueado;
        doc.Bloqueo = new InfoBloqueo
        {
            UsuarioBloqueador = solicitud.Usuario,
            RolBloqueador = rol,
            FechaBloqueo = ahora,
            FechaExpiracion = ahora.AddMinutes(MinutosBloqueo)
        };

        RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.BLOQUEO_EDICION,
            $"Bloqueo de edición asignado por {MinutosBloqueo} minutos. Expira: {doc.Bloqueo.FechaExpiracion:HH:mm:ss}");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException) // concurrencia: otro usuario bloqueó primero
        {
            _context.ChangeTracker.Clear();
            var actual = await CargarDocumentoAsync(id);
            if (actual?.Bloqueo != null) return Conflicto(actual);
            throw;
        }

        return Ok(new { mensaje = $"Documento bloqueado exitosamente hasta las {doc.Bloqueo.FechaExpiracion:HH:mm:ss}." });
    }

    // ═══════════════════════════════════════════════════════════
    //  EXTENSIÓN DE TIEMPO
    // ═══════════════════════════════════════════════════════════

    /// <summary>Extender el tiempo de bloqueo activo en +15 minutos.</summary>
    [HttpPost("{id}/extend-lock")]
    public async Task<IActionResult> ExtenderBloqueo(int id, [FromBody] SolicitudExtension solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return RolInvalido(solicitud.Rol, false);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return Denegado($"El rol '{rol}' no tiene permisos para extender bloqueos.");

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);

        if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
            return Denegado("El documento no está bloqueado actualmente.");

        if (doc.Bloqueo.EstaExpirado)
        {
            await LiberarAsync(doc);
            await _context.SaveChangesAsync();
            return Denegado("El bloqueo ha expirado. Debe solicitar un nuevo bloqueo.");
        }

        if (doc.Bloqueo.UsuarioBloqueador != solicitud.Usuario)
            return Denegado($"Solo el usuario '{doc.Bloqueo.UsuarioBloqueador}' (dueño del bloqueo) puede extender el tiempo.");

        doc.Bloqueo.FechaExpiracion = doc.Bloqueo.FechaExpiracion.AddMinutes(MinutosExtension);

        RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.EXTENSION_TIEMPO,
            $"Tiempo de bloqueo extendido +{MinutosExtension} min. Nueva expiración: {doc.Bloqueo.FechaExpiracion:HH:mm:ss}");

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = $"Tiempo extendido exitosamente. Nueva expiración: {doc.Bloqueo.FechaExpiracion:HH:mm:ss}." });
    }

    // ═══════════════════════════════════════════════════════════
    //  CHECK-IN (NUEVA VERSIÓN CON ARCHIVO FÍSICO)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Subir una nueva versión del documento (Check-in) como multipart/form-data.
    /// Guarda el binario en /Storage y calcula el SHA-256 real leyendo el archivo.
    /// </summary>
    [HttpPost("{id}/checkin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxTamanoArchivo)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxTamanoArchivo)]
    public async Task<IActionResult> Checkin(int id,
        [FromForm] IFormFile? file,
        [FromForm] string? comentarios,
        [FromForm] string? usuario,
        [FromForm] string? rol,
        [FromForm] bool versionMayor = false)
    {
        if (!Enum.TryParse<RolUsuario>(rol ?? string.Empty, true, out var rolParseado))
            return RolInvalido(rol ?? string.Empty, false);

        if (rolParseado != RolUsuario.Editor && rolParseado != RolUsuario.Administrador)
            return Denegado($"El rol '{rolParseado}' no tiene permisos para subir versiones.");

        if (string.IsNullOrWhiteSpace(usuario))
            return Denegado("El usuario es obligatorio.");

        if (string.IsNullOrWhiteSpace(comentarios))
            return Denegado("El comentario de cambios es obligatorio.");

        if (file == null || file.Length == 0)
            return Denegado("Debe adjuntar un archivo válido (no vacío).");

        if (file.Length > MaxTamanoArchivo)
            return Denegado($"El archivo excede el tamaño máximo permitido de {MaxTamanoArchivo / (1024 * 1024)} MB.");

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);

        if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
            return Denegado("El documento no está bloqueado. Debe hacer check-out primero.");

        if (doc.Bloqueo.EstaExpirado)
        {
            await LiberarAsync(doc);
            await _context.SaveChangesAsync();
            return Denegado("El bloqueo ha expirado. El check-in ya no es válido.");
        }

        if (doc.Bloqueo.UsuarioBloqueador != usuario)
            return Denegado($"Solo el usuario '{doc.Bloqueo.UsuarioBloqueador}' (dueño del bloqueo) puede hacer check-in.");

        // El archivo debe corresponder al tipo del documento (.docx, .xlsx, .pptx, .pdf)
        string nombreSeguro = SanitizarNombre(file.FileName);
        string extensionEsperada = ExtensionPorTipo(doc.Tipo);
        if (!string.Equals(Path.GetExtension(nombreSeguro), extensionEsperada, StringComparison.OrdinalIgnoreCase))
            return Denegado($"El archivo debe ser de tipo '{extensionEsperada}' para un documento {doc.Tipo}.");

        // Calcular nueva versión
        int major = 1, minor = 0;
        var partes = doc.VersionActual.Replace("v", "").Split('.');
        if (partes.Length == 2)
        {
            int.TryParse(partes[0], out major);
            int.TryParse(partes[1], out minor);
        }

        if (versionMayor) { major++; minor = 0; }
        else { minor++; }

        // Documento creado sin archivo inicial: el primer check-in genera la v1.0
        if (doc.HistorialVersiones.Count == 0) { major = 1; minor = 0; }

        string nuevaVersion = $"v{major}.{minor}";
        var ahora = DateTime.UtcNow;

        int siguienteNumero = doc.HistorialVersiones.Count == 0
            ? 1
            : doc.HistorialVersiones.Max(v => v.NumeroVersion) + 1;

        // Guardado físico: {id}_{vX.Y}_{nombre}  (nuevaVersion ya incluye la "v")
        Directory.CreateDirectory(_storagePath);
        string nombreAlmacenado = $"{id}_{nuevaVersion}_{nombreSeguro}";
        string rutaCompleta = Path.Combine(_storagePath, nombreAlmacenado);
        string hash;

        try
        {
            await using (var destino = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(destino);
            }

            // SHA-256 REAL: se calcula leyendo el FileStream del archivo ya guardado
            await using (var lectura = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(lectura)).ToLowerInvariant();
            }

            doc.HistorialVersiones.Add(new VersionDocumento
            {
                NumeroVersion = siguienteNumero,
                VersionTexto = nuevaVersion,
                NombreArchivo = nombreSeguro,
                RutaArchivo = nombreAlmacenado,
                Autor = usuario,
                Comentario = comentarios,
                HashSHA256 = hash,
                FechaCreacion = ahora,
                TamanoBytes = file.Length
            });

            doc.VersionActual = nuevaVersion;
            doc.NumeroVersionActual = siguienteNumero;
            doc.FechaUltimaModificacion = ahora;

            // Liberar bloqueo automáticamente
            await LiberarAsync(doc);

            RegistrarEnBitacora(usuario, rolParseado, doc, AccionBitacora.NUEVA_VERSION,
                $"Nueva versión {nuevaVersion} subida. Archivo: {nombreSeguro} ({file.Length} bytes). Comentario: {comentarios}. Hash: {hash[..16]}...");

            await _context.SaveChangesAsync();
        }
        catch
        {
            // Evitar archivos huérfanos si falla la base de datos o la escritura
            if (System.IO.File.Exists(rutaCompleta))
                System.IO.File.Delete(rutaCompleta);
            throw;
        }

        return Ok(new { mensaje = $"Check-in exitoso. Nueva versión: {nuevaVersion}. Hash: {hash[..16]}..." });
    }

    // ═══════════════════════════════════════════════════════════
    //  DESCARGA DE VERSIONES
    // ═══════════════════════════════════════════════════════════

    /// <summary>Descargar el archivo físico de una versión específica (todos los roles pueden consultar).</summary>
    [HttpGet("{id}/download/{versionId}")]
    public async Task<IActionResult> Descargar(int id, int versionId)
    {
        var version = await _context.Versiones
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.DocumentoId == id);

        if (version == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"La versión {versionId} no existe para el documento {id}." });

        if (string.IsNullOrEmpty(version.RutaArchivo))
            return NotFound(new RespuestaError { Error = "Sin archivo", Detalle = "Esta versión no tiene un archivo físico asociado (dato de prueba inicial)." });

        // Protección contra path traversal: la ruta resuelta debe quedar dentro de /Storage
        string rutaCompleta = Path.GetFullPath(Path.Combine(_storagePath, version.RutaArchivo));
        if (!rutaCompleta.StartsWith(_storagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new RespuestaError { Error = "Ruta inválida", Detalle = "La ruta del archivo no es válida." });

        if (!System.IO.File.Exists(rutaCompleta))
            return NotFound(new RespuestaError { Error = "Archivo no disponible", Detalle = "El archivo físico no se encuentra en el almacenamiento del servidor." });

        var stream = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, ObtenerMimeType(Path.GetExtension(rutaCompleta)), version.NombreArchivo, enableRangeProcessing: true);
    }

    // ═══════════════════════════════════════════════════════════
    //  ALTA DE DOCUMENTO (SOLO ADMINISTRADOR)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Crear un documento nuevo (multipart/form-data). El archivo es opcional: si se adjunta,
    /// se guarda como v1.0 con su SHA-256 real; si no, el primer check-in generará la v1.0.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxTamanoArchivo)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxTamanoArchivo)]
    public async Task<IActionResult> CrearDocumento(
        [FromForm] string? codigo,
        [FromForm] string? nombre,
        [FromForm] string? tipo,
        [FromForm] string? departamento,
        [FromForm] string? usuario,
        [FromForm] string? rol,
        [FromForm] IFormFile? file)
    {
        if (!Enum.TryParse<RolUsuario>(rol ?? string.Empty, true, out var rolParseado))
            return RolInvalido(rol ?? string.Empty, false);

        if (rolParseado != RolUsuario.Administrador)
            return Denegado("Solo un Administrador puede crear documentos.");

        if (string.IsNullOrWhiteSpace(usuario))
            return Denegado("El usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(codigo))
            return Denegado("El código del documento es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            return Denegado("El nombre del documento es obligatorio.");
        if (string.IsNullOrWhiteSpace(departamento))
            return Denegado("El departamento/carpeta es obligatorio.");

        codigo = codigo.Trim();
        nombre = nombre.Trim();
        departamento = departamento.Trim();

        if (codigo.Length > 30) return Denegado("El código no puede exceder 30 caracteres.");
        if (nombre.Length > 200) return Denegado("El nombre no puede exceder 200 caracteres.");
        if (departamento.Length > 100) return Denegado("El departamento no puede exceder 100 caracteres.");

        if (!Enum.TryParse<TipoDocumento>(tipo ?? string.Empty, true, out var tipoDoc) || !Enum.IsDefined(tipoDoc))
            return Denegado("Tipo de documento inválido. Use: Word, Excel, PowerPoint o PDF.");

        if (await _context.Documentos.AnyAsync(d => d.Codigo == codigo))
            return CodigoDuplicado(codigo);

        string? nombreSeguro = null;
        if (file != null)
        {
            if (file.Length == 0)
                return Denegado("El archivo adjunto está vacío.");
            if (file.Length > MaxTamanoArchivo)
                return Denegado($"El archivo excede el tamaño máximo permitido de {MaxTamanoArchivo / (1024 * 1024)} MB.");

            nombreSeguro = SanitizarNombre(file.FileName);
            string extensionEsperada = ExtensionPorTipo(tipoDoc);
            if (!string.Equals(Path.GetExtension(nombreSeguro), extensionEsperada, StringComparison.OrdinalIgnoreCase))
                return Denegado($"El archivo debe ser de tipo '{extensionEsperada}' para un documento {tipoDoc}.");
        }

        // Los Ids sembrados son explícitos (sin IDENTITY), por eso se calcula el siguiente.
        int nuevoId = (await _context.Documentos.MaxAsync(d => (int?)d.Id) ?? 0) + 1;
        var ahora = DateTime.UtcNow;

        var doc = new Documento
        {
            Id = nuevoId,
            Codigo = codigo,
            Nombre = nombre,
            Tipo = tipoDoc,
            Departamento = departamento,
            Estado = EstadoDocumento.Disponible,
            EstadoRevision = EstadoRevision.Aprobado,
            VersionActual = file != null ? "v1.0" : "v0.0",
            NumeroVersionActual = file != null ? 1 : 0,
            FechaCreacion = ahora,
            FechaUltimaModificacion = ahora
        };

        string? nombreAlmacenado = null;
        try
        {
            string observaciones = $"Documento dado de alta: {codigo} - {nombre} ({tipoDoc}) en carpeta '{departamento}'.";

            if (file != null && nombreSeguro != null)
            {
                nombreAlmacenado = $"{nuevoId}_v1.0_{nombreSeguro}";
                string hash = await GuardarArchivoAsync(file, nombreAlmacenado);

                doc.HistorialVersiones.Add(new VersionDocumento
                {
                    NumeroVersion = 1,
                    VersionTexto = "v1.0",
                    NombreArchivo = nombreSeguro,
                    RutaArchivo = nombreAlmacenado,
                    Autor = usuario,
                    Comentario = "Versión inicial del documento",
                    HashSHA256 = hash,
                    FechaCreacion = ahora,
                    TamanoBytes = file.Length
                });
                observaciones += $" Versión inicial v1.0 ({file.Length} bytes). Hash: {hash[..16]}...";
            }
            else
            {
                observaciones += " Sin archivo inicial (la v1.0 se generará en el primer check-in).";
            }

            _context.Documentos.Add(doc);
            RegistrarEnBitacora(usuario, rolParseado, doc, AccionBitacora.ALTA_DOCUMENTO, observaciones);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            BorrarArchivoFisico(nombreAlmacenado);
            return Conflict(new RespuestaError
            {
                Error = "Conflicto al crear",
                Detalle = "No se pudo crear el documento: el código ya existe o hubo un alta simultánea. Intente de nuevo."
            });
        }
        catch
        {
            BorrarArchivoFisico(nombreAlmacenado);
            throw;
        }

        return Ok(new { mensaje = $"Documento '{codigo}' creado exitosamente.", id = nuevoId });
    }

    // ═══════════════════════════════════════════════════════════
    //  BAJA DE DOCUMENTO (SOLO ADMINISTRADOR)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Eliminar un documento con sus versiones, bloqueo y archivos físicos. La bitácora conserva el rastro.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarDocumento(int id, [FromQuery] string? usuario, [FromQuery] string? rol)
    {
        if (!Enum.TryParse<RolUsuario>(rol ?? string.Empty, true, out var rolParseado))
            return RolInvalido(rol ?? string.Empty, false);

        if (rolParseado != RolUsuario.Administrador)
            return Denegado("Solo un Administrador puede eliminar documentos.");

        if (string.IsNullOrWhiteSpace(usuario))
            return Denegado("El usuario es obligatorio.");

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);

        var archivos = doc.HistorialVersiones
            .Where(v => !string.IsNullOrEmpty(v.RutaArchivo))
            .Select(v => v.RutaArchivo!)
            .ToList();

        string observaciones =
            $"Documento eliminado: {doc.Codigo} - {doc.Nombre}. Versiones eliminadas: {doc.HistorialVersiones.Count}. Archivos físicos: {archivos.Count}.";
        if (doc.Bloqueo != null && !doc.Bloqueo.EstaExpirado)
            observaciones += $" Estaba bloqueado por '{doc.Bloqueo.UsuarioBloqueador}'.";

        RegistrarEnBitacora(usuario, rolParseado, doc, AccionBitacora.BAJA_DOCUMENTO, observaciones);

        // Las versiones y el bloqueo se eliminan en cascada
        _context.Documentos.Remove(doc);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new RespuestaError
            {
                Error = "Conflicto de concurrencia",
                Detalle = "El documento fue modificado por otro usuario mientras se eliminaba. Actualice e intente de nuevo."
            });
        }

        // Solo tras confirmar la baja en BD se limpian los binarios
        foreach (var archivo in archivos)
            BorrarArchivoFisico(archivo);

        return Ok(new { mensaje = $"Documento '{doc.Codigo}' eliminado correctamente." });
    }

    // ═══════════════════════════════════════════════════════════
    //  FLUJO DE REVISIÓN
    // ═══════════════════════════════════════════════════════════

    /// <summary>Solicitar revisión de un documento libre (Editor o Administrador).</summary>
    [HttpPost("{id}/request-review")]
    public async Task<IActionResult> SolicitarRevision(int id, [FromBody] SolicitudRevision solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return RolInvalido(solicitud.Rol, false);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return Denegado($"El rol '{rol}' no tiene permisos para solicitar revisiones.");

        await LiberarBloqueosExpiradosAsync();

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);

        if (doc.Estado == EstadoDocumento.Bloqueado)
            return Denegado($"No se puede solicitar revisión: el documento está bloqueado por '{doc.Bloqueo?.UsuarioBloqueador}'.");

        if (doc.EstadoRevision == EstadoRevision.EnRevision)
            return Denegado("El documento ya se encuentra en revisión.");

        if (doc.HistorialVersiones.Count == 0)
            return Denegado("El documento no tiene ninguna versión cargada para revisar.");

        doc.EstadoRevision = EstadoRevision.EnRevision;
        doc.RevisadoPor = null;
        doc.ComentarioRevision = null;

        RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.SOLICITUD_REVISION,
            $"Revisión solicitada para la versión {doc.VersionActual}.");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto409Concurrencia();
        }

        return Ok(new { mensaje = $"Revisión solicitada para '{doc.Codigo}' (versión {doc.VersionActual})." });
    }

    /// <summary>Dictaminar (aprobar/rechazar) un documento en revisión (Revisor o Administrador).</summary>
    [HttpPost("{id}/review")]
    public async Task<IActionResult> Revisar(int id, [FromBody] SolicitudDictamen solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return RolInvalido(solicitud.Rol, false);

        if (rol != RolUsuario.Revisor && rol != RolUsuario.Administrador)
            return Denegado($"El rol '{rol}' no tiene permisos para dictaminar revisiones.");

        if (string.IsNullOrWhiteSpace(solicitud.Comentario))
            return Denegado("El comentario del dictamen es obligatorio.");

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);

        if (doc.EstadoRevision != EstadoRevision.EnRevision)
            return Denegado("Solo se pueden dictaminar documentos en estado 'En Revisión'.");

        var comentario = solicitud.Comentario.Trim();
        doc.EstadoRevision = solicitud.Aprobar ? EstadoRevision.Aprobado : EstadoRevision.Rechazado;
        doc.RevisadoPor = solicitud.Usuario;
        doc.ComentarioRevision = comentario;

        string resultado = solicitud.Aprobar ? "APROBADA" : "RECHAZADA";
        RegistrarEnBitacora(solicitud.Usuario, rol, doc,
            solicitud.Aprobar ? AccionBitacora.REVISION_APROBADA : AccionBitacora.REVISION_RECHAZADA,
            $"Revisión {resultado} para la versión {doc.VersionActual}. Dictamen: {comentario}");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto409Concurrencia();
        }

        return Ok(new { mensaje = $"Revisión {resultado.ToLowerInvariant()} para '{doc.Codigo}'." });
    }

    // ═══════════════════════════════════════════════════════════
    //  DESBLOQUEO FORZADO (ADMIN)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Desbloqueo administrativo forzado con motivo obligatorio.</summary>
    [HttpPost("{id}/force-unlock")]
    public async Task<IActionResult> DesbloqueoForzado(int id, [FromBody] SolicitudDesbloqueoForzado solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return RolInvalido(solicitud.Rol, false);

        if (rol != RolUsuario.Administrador)
            return Denegado("Solo un Administrador puede realizar un desbloqueo forzado.");

        if (string.IsNullOrWhiteSpace(solicitud.Motivo))
            return Denegado("El motivo del desbloqueo forzado es obligatorio.");

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);

        if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
            return Denegado("El documento no está bloqueado actualmente.");

        string usuarioAnterior = doc.Bloqueo.UsuarioBloqueador;
        await LiberarAsync(doc);

        RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.DESBLOQUEO_FORZADO,
            $"Desbloqueo forzado ejecutado. Usuario afectado: {usuarioAnterior}. Motivo: {solicitud.Motivo}");

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = $"Desbloqueo forzado exitoso. Se liberó el bloqueo de '{usuarioAnterior}'." });
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS PRIVADOS
    // ═══════════════════════════════════════════════════════════

    private Task<Documento?> CargarDocumentoAsync(int id) =>
        _context.Documentos.Include(d => d.Bloqueo).FirstOrDefaultAsync(d => d.Id == id);

    /// <summary>Libera (en el contexto, sin guardar) el bloqueo de un documento.</summary>
    private Task LiberarAsync(Documento doc)
    {
        if (doc.Bloqueo != null)
        {
            _context.Bloqueos.Remove(doc.Bloqueo);
            doc.Bloqueo = null;
        }
        doc.Estado = EstadoDocumento.Disponible;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Limpieza defensiva: si una petición HTTP llega justo antes de que el Worker pase,
    /// también libera bloqueos vencidos y registra EXPIRACION_AUTOMATICA en la bitácora.
    /// </summary>
    private async Task LiberarBloqueosExpiradosAsync()
    {
        var ahora = DateTime.UtcNow;
        var vencidos = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Where(d => d.Bloqueo != null && d.Bloqueo.FechaExpiracion < ahora)
            .ToListAsync();

        if (vencidos.Count == 0) return;

        foreach (var doc in vencidos)
        {
            // Capturar datos del bloqueo antes de eliminarlo
            var usuarioBloqueador = doc.Bloqueo!.UsuarioBloqueador;
            var rolBloqueador = doc.Bloqueo.RolBloqueador;

            await LiberarAsync(doc);

            RegistrarEnBitacora(usuarioBloqueador, rolBloqueador, doc, AccionBitacora.EXPIRACION_AUTOMATICA,
                $"Bloqueo liberado automáticamente al detectar expiración en petición HTTP. " +
                $"Usuario afectado: {usuarioBloqueador} ({rolBloqueador}).");
        }

        await _context.SaveChangesAsync();
    }

    private void RegistrarEnBitacora(string usuario, RolUsuario rol, Documento doc,
        AccionBitacora accion, string observaciones)
    {
        // Folio lo genera la base de datos (IDENTITY) y la bitácora solo admite inserciones.
        _context.Bitacora.Add(new RegistroBitacora
        {
            FechaHora = DateTime.UtcNow,
            Usuario = usuario,
            Rol = rol,
            CodigoDocumento = doc.Codigo,
            NombreDocumento = doc.Nombre,
            Version = doc.VersionActual,
            Accion = accion,
            Observaciones = observaciones
        });
    }

    /// <summary>Guarda el binario en /Storage y devuelve su SHA-256 real, calculado leyendo el FileStream.</summary>
    private async Task<string> GuardarArchivoAsync(IFormFile file, string nombreAlmacenado)
    {
        Directory.CreateDirectory(_storagePath);
        string ruta = Path.Combine(_storagePath, nombreAlmacenado);

        await using (var destino = new FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(destino);
        }

        await using var lectura = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(lectura)).ToLowerInvariant();
    }

    /// <summary>Borra un archivo de /Storage sin lanzar excepciones (con protección contra path traversal).</summary>
    private void BorrarArchivoFisico(string? nombreAlmacenado)
    {
        if (string.IsNullOrEmpty(nombreAlmacenado)) return;
        try
        {
            string ruta = Path.GetFullPath(Path.Combine(_storagePath, nombreAlmacenado));
            if (ruta.StartsWith(_storagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && System.IO.File.Exists(ruta))
                System.IO.File.Delete(ruta);
        }
        catch (IOException) { /* limpieza best-effort */ }
        catch (UnauthorizedAccessException) { /* limpieza best-effort */ }
    }

    private ConflictObjectResult CodigoDuplicado(string codigo) =>
        Conflict(new RespuestaError { Error = "Código duplicado", Detalle = $"Ya existe un documento con el código '{codigo}'." });

    private ConflictObjectResult Conflicto409Concurrencia() =>
        Conflict(new RespuestaError
        {
            Error = "Conflicto de concurrencia",
            Detalle = "El documento fue modificado por otro usuario. Actualice e intente de nuevo."
        });

    /// <summary>Elimina rutas y caracteres inválidos del nombre original subido por el cliente.</summary>
    private static string SanitizarNombre(string nombre)
    {
        var limpio = Path.GetFileName(nombre.Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(c, '_');
        limpio = limpio.Replace(' ', '_');

        if (limpio.Length > 120)
        {
            var ext = Path.GetExtension(limpio);
            limpio = limpio[..(120 - ext.Length)] + ext;
        }
        return limpio;
    }

    private static string ExtensionPorTipo(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.Word => ".docx",
        TipoDocumento.Excel => ".xlsx",
        TipoDocumento.PowerPoint => ".pptx",
        _ => ".pdf"
    };

    private static string ObtenerMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static DocumentoResumen MapearAResumen(Documento doc) => new()
    {
        Id = doc.Id,
        Codigo = doc.Codigo,
        Nombre = doc.Nombre,
        Tipo = doc.Tipo.ToString(),
        Departamento = doc.Departamento,
        VersionActual = doc.VersionActual,
        Estado = doc.Estado.ToString(),
        EstadoRevision = doc.EstadoRevision.ToString(),
        RevisadoPor = doc.RevisadoPor,
        ComentarioRevision = doc.ComentarioRevision,
        Bloqueo = doc.Bloqueo != null ? new BloqueoResumen
        {
            Usuario = doc.Bloqueo.UsuarioBloqueador,
            Rol = doc.Bloqueo.RolBloqueador.ToString(),
            FechaBloqueo = doc.Bloqueo.FechaBloqueo,
            FechaExpiracion = doc.Bloqueo.FechaExpiracion,
            Expirado = doc.Bloqueo.EstaExpirado
        } : null,
        FechaCreacion = doc.FechaCreacion,
        FechaUltimaModificacion = doc.FechaUltimaModificacion
    };

    private NotFoundObjectResult NoEncontrado(int id) =>
        NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"Documento con Id {id} no existe." });

    private BadRequestObjectResult Denegado(string detalle) =>
        BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = detalle });

    private BadRequestObjectResult RolInvalido(string rol, bool detallado) =>
        BadRequest(new RespuestaError
        {
            Error = "Rol inválido",
            Detalle = detallado
                ? $"El rol '{rol}' no es válido. Use: Lector, Editor o Administrador."
                : $"El rol '{rol}' no es válido."
        });

    private ConflictObjectResult Conflicto(Documento doc) =>
        Conflict(new RespuestaError
        {
            Error = "Conflicto de bloqueo",
            Detalle = $"CONFLICTO: El documento ya está bloqueado por '{doc.Bloqueo!.UsuarioBloqueador}' hasta las {doc.Bloqueo.FechaExpiracion:HH:mm:ss dd/MM/yyyy}."
        });
}
