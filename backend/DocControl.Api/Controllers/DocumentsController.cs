using System.Security.Cryptography;
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
    private const long MaxTamanoArchivo = 500L * 1024 * 1024; // 500 MB (RF-06)
    private const int MaxLongitudTexto = 1000;
    private const int MaxLongitudEtiquetas = 500;
    private static readonly string[] ColoresPermitidos = ["Rojo", "Amarillo", "Azul", "Verde", "Morado", "Cian"];

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

    /// <summary>
    /// Obtener documentos con su estado actual. Por defecto solo los activos;
    /// con <c>?incluirEliminados=true</c> también los de la papelera (propiedad <c>eliminado</c>).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerDocumentos([FromQuery] bool incluirEliminados = false)
    {
        await LiberarBloqueosExpiradosAsync();

        var query = _context.Documentos.AsNoTracking().Include(d => d.Bloqueo).AsQueryable();
        if (!incluirEliminados)
            query = query.Where(d => !d.Eliminado);

        var docs = await query.OrderBy(d => d.Id).ToListAsync();
        return Ok(docs.Select(MapearAResumen).ToList());
    }

    /// <summary>Obtener únicamente los documentos de la papelera (RF-10).</summary>
    [HttpGet("papelera")]
    public async Task<IActionResult> ObtenerPapelera()
    {
        var docs = await _context.Documentos
            .AsNoTracking()
            .Where(d => d.Eliminado)
            .OrderByDescending(d => d.FechaEliminacion)
            .ToListAsync();

        return Ok(docs.Select(MapearAResumen).ToList());
    }

    /// <summary>Obtener KPIs de resumen del workspace (los documentos en papelera no cuentan como activos).</summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> ObtenerKpis()
    {
        await LiberarBloqueosExpiradosAsync();

        var activos = _context.Documentos.Where(d => !d.Eliminado);
        var total = await activos.CountAsync();
        var bloqueados = await activos.CountAsync(d => d.Estado == EstadoDocumento.Bloqueado);

        return Ok(new ResumenKpis
        {
            TotalDocumentos = total,
            DocumentosDisponibles = total - bloqueados,
            DocumentosBloqueados = bloqueados,
            RegistrosBitacora = await _context.Bitacora.CountAsync(),
            DocumentosEnPapelera = await _context.Documentos.CountAsync(d => d.Eliminado)
        });
    }

    /// <summary>Obtener historial de versiones de un documento (también disponible en papelera, solo lectura).</summary>
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
    //  GESTIÓN DE CARPETAS / FOLDERS (JERARQUÍA MULTINIVEL RF-04)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Obtener todas las carpetas (raíz y subcarpetas) con conteos de archivos directos y subcarpetas.</summary>
    [HttpGet("folders")]
    public async Task<IActionResult> ObtenerCarpetas()
    {
        var carpetas = await _context.Carpetas
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .ToListAsync();

        var documentosActivos = await _context.Documentos
            .AsNoTracking()
            .Where(d => !d.Eliminado)
            .Select(d => new { d.CarpetaId, d.Departamento })
            .ToListAsync();

        var resultado = carpetas.Select(c =>
        {
            int totalArchivos = documentosActivos.Count(d =>
                d.CarpetaId == c.Id ||
                (d.CarpetaId == null && c.CarpetaPadreId == null &&
                 string.Equals(d.Departamento, c.Nombre, StringComparison.OrdinalIgnoreCase)));

            int totalSubcarpetas = carpetas.Count(sub => sub.CarpetaPadreId == c.Id);

            return new CarpetaResumen
            {
                Id = c.Id,
                Nombre = c.Nombre,
                CreadoPor = c.CreadoPor,
                FechaCreacion = c.FechaCreacion,
                Color = c.Color,
                CarpetaPadreId = c.CarpetaPadreId,
                TotalArchivos = totalArchivos,
                TotalSubcarpetas = totalSubcarpetas,
                TotalDocumentos = totalArchivos
            };
        }).ToList();

        return Ok(resultado);
    }

    /// <summary>Crear una nueva carpeta raíz o subcarpeta (Solo Administrador o Revisor).</summary>
    [HttpPost("folders")]
    public async Task<IActionResult> CrearCarpeta([FromBody] SolicitudCrearCarpeta solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Administrador && rol != RolUsuario.Revisor)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, null,
                "crear carpetas (solo Administrador o Revisor)",
                codigoReferencia: "FOLDER", nombreReferencia: solicitud.Nombre);

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return Denegado("El usuario es obligatorio.");

        var nombre = solicitud.Nombre?.Trim() ?? string.Empty;
        if (nombre.Length == 0)
            return Denegado("El nombre del folder es obligatorio.");
        if (nombre.Length > 100)
            return Denegado("El nombre del folder no puede exceder 100 caracteres.");

        var color = ColoresPermitidos.FirstOrDefault(c => string.Equals(c, solicitud.Color?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (color == null)
            return Denegado("Color inválido. Use: Rojo, Amarillo, Azul, Verde, Morado o Cian.");

        Carpeta? carpetaPadre = null;
        if (solicitud.CarpetaPadreId.HasValue)
        {
            carpetaPadre = await _context.Carpetas
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == solicitud.CarpetaPadreId.Value);

            if (carpetaPadre == null)
                return NotFound(new RespuestaError
                {
                    Error = "Carpeta padre no encontrada",
                    Detalle = $"La carpeta padre con Id {solicitud.CarpetaPadreId.Value} no existe."
                });
        }

        if (await _context.Carpetas.AnyAsync(c => c.CarpetaPadreId == solicitud.CarpetaPadreId && c.Nombre == nombre))
        {
            string ubicacion = carpetaPadre != null ? $"dentro de '{carpetaPadre.Nombre}'" : "en el nivel raíz";
            return Conflict(new RespuestaError
            {
                Error = "Carpeta duplicada",
                Detalle = $"Ya existe una carpeta con el nombre '{nombre}' {ubicacion}."
            });
        }

        var ahora = DateTime.UtcNow;
        var carpeta = new Carpeta
        {
            Nombre = nombre,
            Color = color,
            CreadoPor = solicitud.Usuario.Trim(),
            FechaCreacion = ahora,
            CarpetaPadreId = solicitud.CarpetaPadreId
        };

        _context.Carpetas.Add(carpeta);

        string detalleBitacora = carpetaPadre != null
            ? $"Subcarpeta '{nombre}' creada dentro de '{carpetaPadre.Nombre}' con color '{color}' por '{carpeta.CreadoPor}'."
            : $"Carpeta raíz '{nombre}' creada con color '{color}' por '{carpeta.CreadoPor}'.";

        RegistrarEnBitacora(solicitud.Usuario.Trim(), rol, "FOLDER", nombre, "-",
            AccionBitacora.ALTA_CARPETA,
            detalleBitacora);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = carpetaPadre != null
                ? $"Subcarpeta '{nombre}' creada exitosamente dentro de '{carpetaPadre.Nombre}'."
                : $"Carpeta '{nombre}' creada exitosamente.",
            carpeta = new CarpetaResumen
            {
                Id = carpeta.Id,
                Nombre = carpeta.Nombre,
                CreadoPor = carpeta.CreadoPor,
                FechaCreacion = carpeta.FechaCreacion,
                Color = carpeta.Color,
                CarpetaPadreId = carpeta.CarpetaPadreId,
                TotalArchivos = 0,
                TotalSubcarpetas = 0,
                TotalDocumentos = 0
            }
        });
    }

    /// <summary>
    /// Modificar únicamente Nombre y Color de una carpeta (Solo Administrador o Revisor).
    /// PROHIBIDO modificar CreadoPor o FechaCreacion. Si el Nombre cambia, actualiza el Departamento
    /// de todos los documentos que pertenecían a ese folder.
    /// </summary>
    [HttpPut("folders/{id}")]
    public async Task<IActionResult> ActualizarCarpeta(int id, [FromBody] SolicitudActualizarCarpeta solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Administrador && rol != RolUsuario.Revisor)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, null,
                "modificar carpetas (solo Administrador o Revisor)",
                codigoReferencia: $"FOLDER-{id}", nombreReferencia: solicitud.Nombre);

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return Denegado("El usuario es obligatorio.");

        var carpeta = await _context.Carpetas.FirstOrDefaultAsync(c => c.Id == id);
        if (carpeta == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"La carpeta con Id {id} no existe." });

        var nuevoNombre = solicitud.Nombre?.Trim() ?? string.Empty;
        if (nuevoNombre.Length == 0)
            return Denegado("El nombre del folder es obligatorio.");
        if (nuevoNombre.Length > 100)
            return Denegado("El nombre del folder no puede exceder 100 caracteres.");

        var nuevoColor = ColoresPermitidos.FirstOrDefault(c => string.Equals(c, solicitud.Color?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (nuevoColor == null)
            return Denegado("Color inválido. Use: Rojo, Amarillo, Azul, Verde, Morado o Cian.");

        if (!string.Equals(carpeta.Nombre, nuevoNombre, StringComparison.OrdinalIgnoreCase) &&
            await _context.Carpetas.AnyAsync(c => c.Id != id && c.CarpetaPadreId == carpeta.CarpetaPadreId && c.Nombre == nuevoNombre))
        {
            return Conflict(new RespuestaError
            {
                Error = "Carpeta duplicada",
                Detalle = $"Ya existe otra carpeta con el nombre '{nuevoNombre}' en este mismo nivel."
            });
        }

        string nombreAnterior = carpeta.Nombre;
        string colorAnterior = carpeta.Color;
        var cambios = new List<string>();

        if (nombreAnterior != nuevoNombre)
        {
            cambios.Add($"Nombre: '{nombreAnterior}' → '{nuevoNombre}'");
            carpeta.Nombre = nuevoNombre;

            // Actualizar automáticamente el Departamento y CarpetaId de los documentos que pertenecían al folder
            var docsAsociados = await _context.Documentos
                .Where(d => d.CarpetaId == carpeta.Id ||
                            (d.CarpetaId == null && carpeta.CarpetaPadreId == null && d.Departamento == nombreAnterior))
                .ToListAsync();

            foreach (var doc in docsAsociados)
            {
                doc.CarpetaId = carpeta.Id;
                doc.Departamento = nuevoNombre;
            }
        }

        if (colorAnterior != nuevoColor)
        {
            cambios.Add($"Color: '{colorAnterior}' → '{nuevoColor}'");
            carpeta.Color = nuevoColor;
        }

        if (cambios.Count == 0)
            return Denegado("No se detectaron cambios en el nombre ni en el color de la carpeta.");

        RegistrarEnBitacora(solicitud.Usuario.Trim(), rol, "FOLDER", carpeta.Nombre, "-",
            AccionBitacora.MODIFICACION_CARPETA,
            $"Carpeta modificada ({ string.Join("; ", cambios) }). CreadoPor ('{carpeta.CreadoPor}') y FechaCreacion preservados intactos.");

        await _context.SaveChangesAsync();

        int totalArchivos = await _context.Documentos.CountAsync(d =>
            !d.Eliminado &&
            (d.CarpetaId == carpeta.Id ||
             (d.CarpetaId == null && carpeta.CarpetaPadreId == null && d.Departamento == carpeta.Nombre)));
        int totalSubcarpetas = await _context.Carpetas.CountAsync(sub => sub.CarpetaPadreId == carpeta.Id);

        return Ok(new
        {
            mensaje = $"Carpeta '{carpeta.Nombre}' actualizada correctamente.",
            carpeta = new CarpetaResumen
            {
                Id = carpeta.Id,
                Nombre = carpeta.Nombre,
                CreadoPor = carpeta.CreadoPor,
                FechaCreacion = carpeta.FechaCreacion,
                Color = carpeta.Color,
                CarpetaPadreId = carpeta.CarpetaPadreId,
                TotalArchivos = totalArchivos,
                TotalSubcarpetas = totalSubcarpetas,
                TotalDocumentos = totalArchivos
            }
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  BLOQUEO (CHECK-OUT)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Bloquear un documento para edición exclusiva (Check-out).</summary>
    [HttpPost("{id}/lock")]
    public async Task<IActionResult> BloquearDocumento(int id, [FromBody] SolicitudBloqueo solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "bloquear documentos (check-out)");

        await LiberarBloqueosExpiradosAsync();

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

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
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "extender bloqueos de edición");

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

        if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
            return Denegado("El documento no está bloqueado actualmente.");

        if (doc.Bloqueo.EstaExpirado)
        {
            LiberarPorExpiracion(doc);
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
    /// Subir una nueva versión del documento (Check-in) como multipart/form-data (hasta 500 MB).
    /// Guarda el binario en /Storage, calcula el SHA-256 real y registra tamaño, qué se hizo y por qué.
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
        [FromForm] string? motivoCambio,
        [FromForm] bool versionMayor = false)
    {
        if (!TryParseRol(rol, out var rolParseado))
            return RolInvalido(rol);

        if (rolParseado != RolUsuario.Editor && rolParseado != RolUsuario.Administrador)
            return await DenegarAccesoAsync(usuario, rolParseado, id, "subir nuevas versiones (check-in)");

        if (string.IsNullOrWhiteSpace(usuario))
            return Denegado("El usuario es obligatorio.");

        if (string.IsNullOrWhiteSpace(comentarios))
            return Denegado("El comentario de cambios es obligatorio.");

        comentarios = comentarios.Trim();
        motivoCambio = motivoCambio?.Trim() ?? string.Empty;

        if (comentarios.Length > MaxLongitudTexto)
            return Denegado($"El comentario no puede exceder {MaxLongitudTexto} caracteres.");
        if (motivoCambio.Length > MaxLongitudTexto)
            return Denegado($"El motivo del cambio no puede exceder {MaxLongitudTexto} caracteres.");

        if (file == null || file.Length == 0)
            return Denegado("Debe adjuntar un archivo válido (no vacío).");

        if (file.Length > MaxTamanoArchivo)
            return Denegado($"El archivo excede el tamaño máximo permitido de {MaxTamanoArchivo / (1024 * 1024)} MB.");

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

        if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
            return Denegado("El documento no está bloqueado. Debe hacer check-out primero.");

        if (doc.Bloqueo.EstaExpirado)
        {
            LiberarPorExpiracion(doc);
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

        // Documento creado sin archivo inicial: el primer check-in genera la v1.0
        string nuevaVersion = doc.HistorialVersiones.Count == 0
            ? "v1.0"
            : CalcularSiguienteVersion(doc.VersionActual, versionMayor);
        var ahora = DateTime.UtcNow;

        int siguienteNumero = doc.HistorialVersiones.Count == 0
            ? 1
            : doc.HistorialVersiones.Max(v => v.NumeroVersion) + 1;

        // Guardado físico: {id}_{vX.Y}_{nombre}  (nuevaVersion ya incluye la "v")
        string nombreAlmacenado = $"{id}_{nuevaVersion}_{nombreSeguro}";

        try
        {
            string hash = await GuardarArchivoAsync(file, nombreAlmacenado);

            doc.HistorialVersiones.Add(new VersionDocumento
            {
                NumeroVersion = siguienteNumero,
                VersionTexto = nuevaVersion,
                NombreArchivo = nombreSeguro,
                RutaArchivo = nombreAlmacenado,
                Autor = usuario,
                Comentario = comentarios,
                MotivoCambio = motivoCambio,
                HashSHA256 = hash,
                FechaCreacion = ahora,
                TamanoBytes = file.Length
            });

            doc.VersionActual = nuevaVersion;
            doc.NumeroVersionActual = siguienteNumero;
            doc.FechaUltimaModificacion = ahora;

            // Liberar bloqueo automáticamente
            LiberarDocumento(doc);

            string observaciones = $"Nueva versión {nuevaVersion} subida. Archivo: {nombreSeguro} ({file.Length} bytes). " +
                                   $"Qué se hizo: {comentarios}.";
            if (motivoCambio.Length > 0) observaciones += $" Por qué: {motivoCambio}.";
            observaciones += $" Hash: {hash[..16]}...";

            RegistrarEnBitacora(usuario, rolParseado, doc, AccionBitacora.NUEVA_VERSION, observaciones);

            await _context.SaveChangesAsync();

            return Ok(new { mensaje = $"Check-in exitoso. Nueva versión: {nuevaVersion}. Hash: {hash[..16]}..." });
        }
        catch
        {
            // Evitar archivos huérfanos si falla la base de datos o la escritura
            BorrarArchivoFisico(nombreAlmacenado);
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  DESCARGA DE VERSIONES
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Descargar el archivo físico de una versión específica (todos los roles pueden consultar).
    /// Registra DESCARGA_ARCHIVO en bitácora. <c>usuario</c> y <c>rol</c> son opcionales por compatibilidad.
    /// </summary>
    [HttpGet("{id}/download/{versionId}")]
    public async Task<IActionResult> Descargar(int id, int versionId, [FromQuery] string? usuario, [FromQuery] string? rol)
    {
        var doc = await _context.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);

        var version = await _context.Versiones
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.DocumentoId == id);

        if (version == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"La versión {versionId} no existe para el documento {id}." });

        if (string.IsNullOrEmpty(version.RutaArchivo))
            return NotFound(new RespuestaError { Error = "Sin archivo", Detalle = "Esta versión no tiene un archivo físico asociado (dato de prueba inicial)." });

        // Protección contra path traversal: la ruta resuelta debe quedar dentro de /Storage
        string? rutaCompleta = ResolverRutaSegura(version.RutaArchivo);
        if (rutaCompleta == null)
            return BadRequest(new RespuestaError { Error = "Ruta inválida", Detalle = "La ruta del archivo no es válida." });

        if (!System.IO.File.Exists(rutaCompleta))
            return NotFound(new RespuestaError { Error = "Archivo no disponible", Detalle = "El archivo físico no se encuentra en el almacenamiento del servidor." });

        // Auditoría de descarga (si no se informa identidad se registra como anónimo con rol de solo lectura)
        var rolDescarga = TryParseRol(rol, out var r) ? r : RolUsuario.Lector;
        string usuarioDescarga = string.IsNullOrWhiteSpace(usuario) ? "Anónimo" : usuario.Trim();
        RegistrarEnBitacora(usuarioDescarga, rolDescarga, doc.Codigo, doc.Nombre, version.VersionTexto,
            AccionBitacora.DESCARGA_ARCHIVO,
            $"Descarga de la versión {version.VersionTexto}. Archivo: {version.NombreArchivo} ({version.TamanoBytes} bytes).");
        await _context.SaveChangesAsync();

        var stream = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, ObtenerMimeType(Path.GetExtension(rutaCompleta)), version.NombreArchivo, enableRangeProcessing: true);
    }

    // ═══════════════════════════════════════════════════════════
    //  RESTAURAR VERSIÓN ANTERIOR COMO VIGENTE (RF-07)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Crea una NUEVA versión consecutiva copiando el contenido de una versión anterior.
    /// El historial previo queda intacto (Criterio de Aceptación #4).
    /// </summary>
    [HttpPost("{id}/restore-version/{versionId}")]
    public async Task<IActionResult> RestaurarVersion(int id, int versionId, [FromBody] SolicitudRestaurarVersion solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "restaurar versiones anteriores");

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return Denegado("El usuario es obligatorio.");

        string motivo = solicitud.Motivo?.Trim() ?? string.Empty;
        if (motivo.Length > MaxLongitudTexto)
            return Denegado($"El motivo no puede exceder {MaxLongitudTexto} caracteres.");

        await LiberarBloqueosExpiradosAsync();

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

        // Solo se bloquea si el documento está tomado por OTRO usuario
        if (doc.Bloqueo != null && doc.Bloqueo.UsuarioBloqueador != solicitud.Usuario)
            return Conflicto(doc);

        if (doc.EstadoRevision == EstadoRevision.EnRevision)
            return Denegado("El documento está en revisión; no se puede cambiar la versión vigente hasta que se emita el dictamen.");

        var origen = doc.HistorialVersiones.FirstOrDefault(v => v.Id == versionId);
        if (origen == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"La versión {versionId} no existe para el documento {id}." });

        var vigente = doc.HistorialVersiones.MaxBy(v => v.NumeroVersion)!;
        if (origen.Id == vigente.Id)
            return Denegado($"La versión {origen.VersionTexto} ya es la versión vigente.");

        string nuevaVersion = CalcularSiguienteVersion(doc.VersionActual, false);
        int siguienteNumero = vigente.NumeroVersion + 1;
        var ahora = DateTime.UtcNow;

        // Copia física independiente del binario (si la versión origen lo tiene)
        string? nuevaRuta = null;
        if (!string.IsNullOrEmpty(origen.RutaArchivo))
        {
            string? rutaOrigen = ResolverRutaSegura(origen.RutaArchivo);
            if (rutaOrigen == null || !System.IO.File.Exists(rutaOrigen))
                return NotFound(new RespuestaError
                {
                    Error = "Archivo no disponible",
                    Detalle = $"El archivo físico de la versión {origen.VersionTexto} no se encuentra en el almacenamiento; no es posible restaurarla."
                });

            nuevaRuta = $"{id}_{nuevaVersion}_{SanitizarNombre(origen.NombreArchivo)}";
            Directory.CreateDirectory(_storagePath);
            System.IO.File.Copy(rutaOrigen, Path.Combine(_storagePath, nuevaRuta), overwrite: true);
        }

        try
        {
            doc.HistorialVersiones.Add(new VersionDocumento
            {
                NumeroVersion = siguienteNumero,
                VersionTexto = nuevaVersion,
                NombreArchivo = origen.NombreArchivo,
                RutaArchivo = nuevaRuta,
                Autor = solicitud.Usuario,
                Comentario = $"Restauración de la versión {origen.VersionTexto}",
                MotivoCambio = motivo,
                HashSHA256 = origen.HashSHA256,
                FechaCreacion = ahora,
                TamanoBytes = origen.TamanoBytes
            });

            string versionAnterior = doc.VersionActual;
            doc.VersionActual = nuevaVersion;
            doc.NumeroVersionActual = siguienteNumero;
            doc.FechaUltimaModificacion = ahora;

            string observaciones = $"Versión {origen.VersionTexto} restaurada como nueva versión vigente {nuevaVersion} " +
                                   $"(anterior vigente: {versionAnterior}). Hash: {origen.HashSHA256[..16]}...";
            if (motivo.Length > 0) observaciones += $" Motivo: {motivo}";

            RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.RESTAURAR_VERSION, observaciones);

            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            BorrarArchivoFisico(nuevaRuta);
            return Conflicto409Concurrencia();
        }
        catch
        {
            BorrarArchivoFisico(nuevaRuta);
            throw;
        }

        return Ok(new { mensaje = $"Versión {origen.VersionTexto} restaurada como {nuevaVersion}." });
    }

    // ═══════════════════════════════════════════════════════════
    //  METADATOS / ETIQUETAS / MOVER CARPETA (RF-03, RF-04, RF-14)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Renombrar, mover de carpeta y/o actualizar etiquetas. Los campos null no se modifican.</summary>
    [HttpPut("{id}/metadata")]
    public async Task<IActionResult> ActualizarMetadatos(int id, [FromBody] SolicitudMetadatos solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "editar metadatos del documento");

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return Denegado("El usuario es obligatorio.");

        await LiberarBloqueosExpiradosAsync();

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

        if (doc.Bloqueo != null && doc.Bloqueo.UsuarioBloqueador != solicitud.Usuario)
            return Conflicto(doc);

        var cambios = new List<string>();

        if (solicitud.Nombre != null)
        {
            var nombre = solicitud.Nombre.Trim();
            if (nombre.Length == 0) return Denegado("El nombre no puede quedar vacío.");
            if (nombre.Length > 200) return Denegado("El nombre no puede exceder 200 caracteres.");
            if (nombre != doc.Nombre)
            {
                cambios.Add($"Nombre: '{doc.Nombre}' → '{nombre}'");
                doc.Nombre = nombre;
            }
        }

        if (solicitud.Departamento != null)
        {
            var depto = solicitud.Departamento.Trim();
            if (depto.Length == 0) return Denegado("El departamento/carpeta no puede quedar vacío.");
            if (depto.Length > 100) return Denegado("El departamento no puede exceder 100 caracteres.");
            if (depto != doc.Departamento)
            {
                cambios.Add($"Carpeta: '{doc.Departamento}' → '{depto}'");
                doc.Departamento = depto;
            }
        }

        if (solicitud.Etiquetas != null)
        {
            var etiquetas = NormalizarEtiquetas(solicitud.Etiquetas);
            if (etiquetas.Length > MaxLongitudEtiquetas)
                return Denegado($"Las etiquetas no pueden exceder {MaxLongitudEtiquetas} caracteres en total.");
            if (etiquetas != doc.Etiquetas)
            {
                cambios.Add($"Etiquetas: '{doc.Etiquetas}' → '{etiquetas}'");
                doc.Etiquetas = etiquetas;
            }
        }

        if (cambios.Count == 0)
            return Denegado("No se detectaron cambios en los metadatos.");

        doc.FechaUltimaModificacion = DateTime.UtcNow;
        RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.ACTUALIZACION_METADATOS,
            "Metadatos actualizados. " + string.Join("; ", cambios) + ".");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto409Concurrencia();
        }

        return Ok(new { mensaje = $"Metadatos de '{doc.Codigo}' actualizados correctamente.", documento = MapearAResumen(doc) });
    }

    /// <summary>
    /// Mover un archivo entre carpetas o subcarpetas existentes (Permitido para Administrador, Revisor y Editor,
    /// siempre que el archivo no esté bloqueado por otro usuario).
    /// </summary>
    [HttpPut("{id}/move")]
    public async Task<IActionResult> MoverDocumento(int id, [FromBody] SolicitudMoverDocumento solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Administrador && rol != RolUsuario.Revisor && rol != RolUsuario.Editor)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "mover archivos entre carpetas");

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return Denegado("El usuario es obligatorio.");

        var destinoNombre = solicitud.CarpetaDestino?.Trim() ?? string.Empty;
        if (!solicitud.CarpetaDestinoId.HasValue && destinoNombre.Length == 0)
            return Denegado("Debe especificar la carpeta destino.");

        await LiberarBloqueosExpiradosAsync();

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

        if (doc.Bloqueo != null && doc.Bloqueo.UsuarioBloqueador != solicitud.Usuario)
            return Conflicto(doc);

        var todasCarpetas = await _context.Carpetas.AsNoTracking().ToListAsync();

        Carpeta? carpetaDestino = null;
        if (solicitud.CarpetaDestinoId.HasValue)
        {
            carpetaDestino = todasCarpetas.FirstOrDefault(c => c.Id == solicitud.CarpetaDestinoId.Value);
        }
        else
        {
            carpetaDestino = todasCarpetas.FirstOrDefault(c => string.Equals(c.Nombre, destinoNombre, StringComparison.OrdinalIgnoreCase));
        }

        if (carpetaDestino == null)
            return NotFound(new RespuestaError
            {
                Error = "Carpeta no encontrada",
                Detalle = solicitud.CarpetaDestinoId.HasValue
                    ? $"La carpeta destino con Id {solicitud.CarpetaDestinoId.Value} no existe."
                    : $"La carpeta destino '{destinoNombre}' no existe."
            });

        bool yaEstaEnDestino = doc.CarpetaId.HasValue
            ? doc.CarpetaId.Value == carpetaDestino.Id
            : (carpetaDestino.CarpetaPadreId == null &&
               string.Equals(doc.Departamento, carpetaDestino.Nombre, StringComparison.OrdinalIgnoreCase));

        if (yaEstaEnDestino)
            return Denegado($"El archivo ya se encuentra en la carpeta '{carpetaDestino.Nombre}'.");

        string rutaOrigen = ConstruirRutaCarpeta(doc.CarpetaId, doc.Departamento, todasCarpetas);
        string rutaDestino = ConstruirRutaCarpeta(carpetaDestino.Id, carpetaDestino.Nombre, todasCarpetas);

        doc.CarpetaId = carpetaDestino.Id;
        doc.Departamento = carpetaDestino.Nombre;
        doc.FechaUltimaModificacion = DateTime.UtcNow;

        RegistrarEnBitacora(solicitud.Usuario.Trim(), rol, doc, AccionBitacora.MOVIMIENTO_CARPETA,
            $"Archivo movido de '{rutaOrigen}' a '{rutaDestino}'.");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto409Concurrencia();
        }

        return Ok(new
        {
            mensaje = $"Archivo '{doc.Codigo}' movido de '{rutaOrigen}' a '{rutaDestino}'.",
            documento = MapearAResumen(doc)
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  ALTA DE DOCUMENTO (ADMINISTRADOR / EDITOR)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Crear un documento nuevo (multipart/form-data). El archivo inicial v1.0 es obligatorio,
    /// el Tipo se detecta automáticamente por la extensión y el Código se autogenera/recalcula si colisiona.
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
        [FromForm] int? carpetaId,
        [FromForm] string? usuario,
        [FromForm] string? rol,
        [FromForm] string? etiquetas,
        [FromForm] IFormFile? file)
    {
        if (!TryParseRol(rol, out var rolParseado))
            return RolInvalido(rol);

        if (rolParseado != RolUsuario.Administrador && rolParseado != RolUsuario.Editor)
            return await DenegarAccesoAsync(usuario, rolParseado, null, "crear documentos (solo Administrador o Editor)",
                codigoReferencia: codigo, nombreReferencia: nombre);

        if (string.IsNullOrWhiteSpace(usuario))
            return Denegado("El usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            return Denegado("El nombre del documento es obligatorio.");
        if (string.IsNullOrWhiteSpace(departamento) && !carpetaId.HasValue)
            return Denegado("El departamento/carpeta es obligatorio.");

        if (file == null || file.Length == 0)
            return Denegado("El archivo inicial v1.0 es obligatorio y no puede estar vacío.");
        if (file.Length > MaxTamanoArchivo)
            return Denegado($"El archivo excede el tamaño máximo permitido de {MaxTamanoArchivo / (1024 * 1024)} MB.");

        string nombreSeguro = SanitizarNombre(file.FileName);
        string extensionArchivo = Path.GetExtension(nombreSeguro);
        var tipoDetectado = DetectarTipoPorExtension(extensionArchivo);
        if (tipoDetectado == null)
            return Denegado("Formato de archivo no soportado. Solo se permiten archivos Word (.docx), Excel (.xlsx), PowerPoint (.pptx) o PDF (.pdf).");

        TipoDocumento tipoDoc = tipoDetectado.Value;

        Carpeta? carpetaDestino = null;
        if (carpetaId.HasValue)
        {
            carpetaDestino = await _context.Carpetas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == carpetaId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(departamento))
        {
            var depTrim = departamento.Trim();
            carpetaDestino = await _context.Carpetas.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CarpetaPadreId == null && c.Nombre == depTrim)
                ?? await _context.Carpetas.AsNoTracking().FirstOrDefaultAsync(c => c.Nombre == depTrim);
        }

        nombre = nombre.Trim();
        departamento = (carpetaDestino?.Nombre ?? departamento ?? string.Empty).Trim();
        string etiquetasNormalizadas = NormalizarEtiquetas(etiquetas);

        if (nombre.Length > 200) return Denegado("El nombre no puede exceder 200 caracteres.");
        if (departamento.Length == 0 || departamento.Length > 100)
            return Denegado("El departamento no puede exceder 100 caracteres.");
        if (etiquetasNormalizadas.Length > MaxLongitudEtiquetas)
            return Denegado($"Las etiquetas no pueden exceder {MaxLongitudEtiquetas} caracteres en total.");

        // Si el código viene vacío o ya existe en la BD, recalcular el consecutivo automáticamente
        string codigoFinal = await ObtenerCodigoDisponibleAsync(departamento, nombre, codigo);
        if (codigoFinal.Length > 30)
            codigoFinal = codigoFinal[..30];

        // Los Ids sembrados son explícitos (sin IDENTITY), por eso se calcula el siguiente.
        int nuevoId = (await _context.Documentos.MaxAsync(d => (int?)d.Id) ?? 0) + 1;
        var ahora = DateTime.UtcNow;

        var doc = new Documento
        {
            Id = nuevoId,
            Codigo = codigoFinal,
            Nombre = nombre,
            Tipo = tipoDoc,
            Departamento = departamento,
            CarpetaId = carpetaDestino?.Id,
            Etiquetas = etiquetasNormalizadas,
            Estado = EstadoDocumento.Disponible,
            EstadoRevision = EstadoRevision.Aprobado,
            VersionActual = "v1.0",
            NumeroVersionActual = 1,
            FechaCreacion = ahora,
            FechaUltimaModificacion = ahora
        };

        string nombreAlmacenado = $"{nuevoId}_v1.0_{nombreSeguro}";
        try
        {
            string hash = await GuardarArchivoAsync(file, nombreAlmacenado);

            doc.HistorialVersiones.Add(new VersionDocumento
            {
                NumeroVersion = 1,
                VersionTexto = "v1.0",
                NombreArchivo = nombreSeguro,
                RutaArchivo = nombreAlmacenado,
                Autor = usuario.Trim(),
                Comentario = "Versión inicial del documento",
                MotivoCambio = "Alta del documento en el sistema",
                HashSHA256 = hash,
                FechaCreacion = ahora,
                TamanoBytes = file.Length
            });

            string observaciones = $"Documento dado de alta: {codigoFinal} - {nombre} ({tipoDoc}) en carpeta '{departamento}'.";
            if (etiquetasNormalizadas.Length > 0) observaciones += $" Etiquetas: {etiquetasNormalizadas}.";
            observaciones += $" Versión inicial v1.0 ({file.Length} bytes). Hash: {hash[..16]}...";

            _context.Documentos.Add(doc);
            RegistrarEnBitacora(usuario.Trim(), rolParseado, doc, AccionBitacora.ALTA_DOCUMENTO, observaciones);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            BorrarArchivoFisico(nombreAlmacenado);
            return Conflict(new RespuestaError
            {
                Error = "Conflicto al crear",
                Detalle = "No se pudo crear el documento por un alta simultánea. Intente de nuevo."
            });
        }
        catch
        {
            BorrarArchivoFisico(nombreAlmacenado);
            throw;
        }

        return Ok(new { mensaje = $"Documento '{codigoFinal}' creado exitosamente.", id = nuevoId, codigo = codigoFinal });
    }

    // ═══════════════════════════════════════════════════════════
    //  BAJA DE DOCUMENTO: PAPELERA / ELIMINACIÓN DEFINITIVA (RF-10)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 1er paso: envía el documento a la papelera (borrado lógico).
    /// 2º paso: si ya está en papelera y se envía <c>?definitivo=true</c>, lo elimina físicamente con sus archivos.
    /// La bitácora conserva el rastro en ambos casos.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarDocumento(int id,
        [FromQuery] string? usuario, [FromQuery] string? rol, [FromQuery] bool definitivo = false)
    {
        if (!TryParseRol(rol, out var rolParseado))
            return RolInvalido(rol);

        if (rolParseado != RolUsuario.Administrador)
            return await DenegarAccesoAsync(usuario, rolParseado, id,
                definitivo ? "eliminar documentos definitivamente (solo Administrador)" : "enviar documentos a la papelera (solo Administrador)");

        if (string.IsNullOrWhiteSpace(usuario))
            return Denegado("El usuario es obligatorio.");

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);

        // ── Paso 1: enviar a la papelera ──
        if (!doc.Eliminado)
        {
            string obsPapelera = $"Enviado a la papelera: {doc.Codigo} - {doc.Nombre}.";
            if (doc.Bloqueo != null)
            {
                obsPapelera += $" Se liberó el bloqueo activo de '{doc.Bloqueo.UsuarioBloqueador}'.";
                LiberarDocumento(doc);
            }

            doc.Eliminado = true;
            doc.FechaEliminacion = DateTime.UtcNow;
            RegistrarEnBitacora(usuario, rolParseado, doc, AccionBitacora.BAJA_DOCUMENTO, obsPapelera);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflicto409Concurrencia();
            }

            return Ok(new { mensaje = $"Documento '{doc.Codigo}' enviado a la papelera." });
        }

        // ── Paso 2: eliminación definitiva ──
        if (!definitivo)
            return Denegado("El documento ya está en la papelera. Para eliminarlo definitivamente envíe ?definitivo=true.");

        var archivos = doc.HistorialVersiones
            .Where(v => !string.IsNullOrEmpty(v.RutaArchivo))
            .Select(v => v.RutaArchivo!)
            .Distinct()
            .ToList();

        RegistrarEnBitacora(usuario, rolParseado, doc, AccionBitacora.ELIMINACION_DEFINITIVA,
            $"Documento eliminado definitivamente desde la papelera: {doc.Codigo} - {doc.Nombre}. " +
            $"Versiones eliminadas: {doc.HistorialVersiones.Count}. Archivos físicos: {archivos.Count}.");

        // Las versiones y el bloqueo se eliminan en cascada
        _context.Documentos.Remove(doc);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto409Concurrencia();
        }

        // Solo tras confirmar la baja en BD se limpian los binarios
        foreach (var archivo in archivos)
            BorrarArchivoFisico(archivo);

        return Ok(new { mensaje = $"Documento '{doc.Codigo}' eliminado definitivamente." });
    }

    /// <summary>Restaurar un documento desde la papelera (solo Administrador).</summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> RestaurarDePapelera(int id, [FromBody] SolicitudUsuario solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "restaurar documentos de la papelera (solo Administrador)");

        if (string.IsNullOrWhiteSpace(solicitud.Usuario))
            return Denegado("El usuario es obligatorio.");

        var doc = await _context.Documentos.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);

        if (!doc.Eliminado)
            return Denegado("El documento no está en la papelera.");

        var fechaEliminacion = doc.FechaEliminacion;
        doc.Eliminado = false;
        doc.FechaEliminacion = null;

        RegistrarEnBitacora(solicitud.Usuario, rol, doc, AccionBitacora.RESTAURAR_PAPELERA,
            $"Documento restaurado desde la papelera (enviado el {fechaEliminacion:dd/MM/yyyy HH:mm:ss} UTC).");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto409Concurrencia();
        }

        return Ok(new { mensaje = $"Documento '{doc.Codigo}' restaurado desde la papelera." });
    }

    // ═══════════════════════════════════════════════════════════
    //  FLUJO DE REVISIÓN
    // ═══════════════════════════════════════════════════════════

    /// <summary>Solicitar revisión de un documento libre (Editor o Administrador).</summary>
    [HttpPost("{id}/request-review")]
    public async Task<IActionResult> SolicitarRevision(int id, [FromBody] SolicitudRevision solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Editor && rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "solicitar revisiones");

        await LiberarBloqueosExpiradosAsync();

        var doc = await _context.Documentos
            .Include(d => d.Bloqueo)
            .Include(d => d.HistorialVersiones)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

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
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Revisor && rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "dictaminar revisiones");

        if (string.IsNullOrWhiteSpace(solicitud.Comentario))
            return Denegado("El comentario del dictamen es obligatorio.");

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);
        if (doc.Eliminado) return EnPapelera(doc);

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
        if (!TryParseRol(solicitud.Rol, out var rol))
            return RolInvalido(solicitud.Rol);

        if (rol != RolUsuario.Administrador)
            return await DenegarAccesoAsync(solicitud.Usuario, rol, id, "realizar desbloqueos forzados (solo Administrador)");

        if (string.IsNullOrWhiteSpace(solicitud.Motivo))
            return Denegado("El motivo del desbloqueo forzado es obligatorio.");

        var doc = await CargarDocumentoAsync(id);
        if (doc == null) return NoEncontrado(id);

        if (doc.Estado != EstadoDocumento.Bloqueado || doc.Bloqueo == null)
            return Denegado("El documento no está bloqueado actualmente.");

        string usuarioAnterior = doc.Bloqueo.UsuarioBloqueador;
        LiberarDocumento(doc);

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

    /// <summary>Parsea el rol rechazando valores numéricos fuera del enum (ej. "99").</summary>
    private static bool TryParseRol(string? valor, out RolUsuario rol) =>
        Enum.TryParse(valor ?? string.Empty, true, out rol) && Enum.IsDefined(rol);

    /// <summary>Libera (en el contexto, sin guardar) el bloqueo de un documento.</summary>
    private void LiberarDocumento(Documento doc)
    {
        if (doc.Bloqueo != null)
        {
            _context.Bloqueos.Remove(doc.Bloqueo);
            doc.Bloqueo = null;
        }
        doc.Estado = EstadoDocumento.Disponible;
    }

    /// <summary>Libera un bloqueo vencido y registra EXPIRACION_AUTOMATICA (sin guardar).</summary>
    private void LiberarPorExpiracion(Documento doc)
    {
        if (doc.Bloqueo == null) return;

        var usuarioBloqueador = doc.Bloqueo.UsuarioBloqueador;
        var rolBloqueador = doc.Bloqueo.RolBloqueador;

        LiberarDocumento(doc);

        RegistrarEnBitacora(usuarioBloqueador, rolBloqueador, doc, AccionBitacora.EXPIRACION_AUTOMATICA,
            $"Bloqueo liberado automáticamente al detectar expiración en petición HTTP. " +
            $"Usuario afectado: {usuarioBloqueador} ({rolBloqueador}).");
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
            LiberarPorExpiracion(doc);

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Criterio de Aceptación #5: registra en bitácora el intento sin permiso (ACCESO_DENEGADO)
    /// y devuelve 403 Forbidden.
    /// </summary>
    private async Task<ObjectResult> DenegarAccesoAsync(string? usuario, RolUsuario rol, int? documentoId,
        string accionIntentada, string? codigoReferencia = null, string? nombreReferencia = null)
    {
        string codigo = string.IsNullOrWhiteSpace(codigoReferencia) ? "N/A" : codigoReferencia.Trim();
        string nombre = string.IsNullOrWhiteSpace(nombreReferencia) ? "(sin documento)" : nombreReferencia.Trim();
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
            else
            {
                codigo = $"ID-{documentoId.Value}";
                nombre = "(documento inexistente)";
            }
        }

        string usuarioTexto = string.IsNullOrWhiteSpace(usuario) ? "Desconocido" : usuario.Trim();
        string detalle = $"El rol '{rol}' no tiene permisos para {accionIntentada}.";

        RegistrarEnBitacora(Recortar(usuarioTexto, 100), rol, Recortar(codigo, 30), Recortar(nombre, 200), version,
            AccionBitacora.ACCESO_DENEGADO, $"Intento de acción sin permiso: {accionIntentada}. {detalle}");

        await _context.SaveChangesAsync();

        return StatusCode(StatusCodes.Status403Forbidden,
            new RespuestaError { Error = "Acceso denegado", Detalle = detalle });
    }

    private void RegistrarEnBitacora(string usuario, RolUsuario rol, Documento doc,
        AccionBitacora accion, string observaciones) =>
        RegistrarEnBitacora(usuario, rol, doc.Codigo, doc.Nombre, doc.VersionActual, accion, observaciones);

    private void RegistrarEnBitacora(string usuario, RolUsuario rol, string codigo, string nombre, string version,
        AccionBitacora accion, string observaciones)
    {
        // Folio lo genera la base de datos (IDENTITY) y la bitácora solo admite inserciones.
        _context.Bitacora.Add(new RegistroBitacora
        {
            FechaHora = DateTime.UtcNow,
            Usuario = usuario,
            Rol = rol,
            CodigoDocumento = codigo,
            NombreDocumento = nombre,
            Version = version,
            Accion = accion,
            Observaciones = Recortar(observaciones, 2000),
            DatosMaquina = ObtenerDatosMaquina()
        });
    }

    /// <summary>Captura automática de IP remota y User-Agent de la petición en curso.</summary>
    private string ObtenerDatosMaquina()
    {
        var ip = HttpContext?.Connection.RemoteIpAddress;
        string ipTexto = ip == null
            ? "desconocida"
            : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString());

        string userAgent = Request?.Headers.UserAgent.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userAgent)) userAgent = "no informado";

        return Recortar($"IP: {ipTexto} | User-Agent: {userAgent}", 500);
    }

    private static string Recortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max];

    /// <summary>Normaliza etiquetas: recorta, quita vacías y duplicadas (sin distinguir mayúsculas).</summary>
    private static string NormalizarEtiquetas(string? etiquetas)
    {
        if (string.IsNullOrWhiteSpace(etiquetas)) return string.Empty;

        var lista = etiquetas
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(",", lista);
    }

    private static List<string> SepararEtiquetas(string etiquetas) =>
        string.IsNullOrWhiteSpace(etiquetas)
            ? new List<string>()
            : etiquetas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Calcula la siguiente versión semántica simple (vX.Y).</summary>
    private static string CalcularSiguienteVersion(string versionActual, bool versionMayor)
    {
        int major = 1, minor = 0;
        var partes = versionActual.Replace("v", "").Split('.');
        if (partes.Length == 2)
        {
            int.TryParse(partes[0], out major);
            int.TryParse(partes[1], out minor);
        }

        if (versionMayor) { major++; minor = 0; }
        else { minor++; }

        return $"v{major}.{minor}";
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

    /// <summary>Resuelve una ruta relativa dentro de /Storage; devuelve null si intenta salir (path traversal).</summary>
    private string? ResolverRutaSegura(string nombreAlmacenado)
    {
        string ruta = Path.GetFullPath(Path.Combine(_storagePath, nombreAlmacenado));
        return ruta.StartsWith(_storagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? ruta
            : null;
    }

    /// <summary>Borra un archivo de /Storage sin lanzar excepciones (con protección contra path traversal).</summary>
    private void BorrarArchivoFisico(string? nombreAlmacenado)
    {
        if (string.IsNullOrEmpty(nombreAlmacenado)) return;
        try
        {
            string? ruta = ResolverRutaSegura(nombreAlmacenado);
            if (ruta != null && System.IO.File.Exists(ruta))
                System.IO.File.Delete(ruta);
        }
        catch (IOException) { /* limpieza best-effort */ }
        catch (UnauthorizedAccessException) { /* limpieza best-effort */ }
    }

    private ConflictObjectResult CodigoDuplicado(string codigo, bool enPapelera) =>
        Conflict(new RespuestaError
        {
            Error = "Código duplicado",
            Detalle = enPapelera
                ? $"Ya existe un documento con el código '{codigo}' en la papelera. Restáurelo o elimínelo definitivamente."
                : $"Ya existe un documento con el código '{codigo}'."
        });

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

    private static TipoDocumento? DetectarTipoPorExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => TipoDocumento.Word,
        ".xlsx" => TipoDocumento.Excel,
        ".pptx" => TipoDocumento.PowerPoint,
        ".pdf" => TipoDocumento.PDF,
        _ => null
    };

    /// <summary>
    /// Valida el código recibido o genera/recalcula automáticamente el consecutivo
    /// con formato [INICIALES_CARPETA]-[INICIALES_DOCUMENTO]-[CONSECUTIVO] para evitar colisiones.
    /// </summary>
    private async Task<string> ObtenerCodigoDisponibleAsync(string departamento, string nombre, string? codigoRecibido)
    {
        var codigosExistentes = (await _context.Documentos
            .AsNoTracking()
            .Select(d => d.Codigo)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(codigoRecibido))
        {
            string limpio = codigoRecibido.Trim().ToUpperInvariant();
            if (!codigosExistentes.Contains(limpio))
                return limpio;
        }

        string iniCarpeta = ObtenerInicialesCarpeta(departamento);
        string iniDoc = ObtenerInicialesDocumento(nombre);
        int consecutivo = codigosExistentes.Count + 1;

        while (true)
        {
            string candidato = $"{iniCarpeta}-{iniDoc}-{consecutivo:D3}";
            if (!codigosExistentes.Contains(candidato))
                return candidato;
            consecutivo++;
        }
    }

    private static string ObtenerInicialesCarpeta(string departamento)
    {
        var palabras = QuitarAcentos(departamento)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant())
            .Where(p => p.Length > 0)
            .ToArray();

        if (palabras.Length == 0) return "GEN";
        if (palabras.Length >= 2)
            return string.Concat(palabras.Select(p => p[0]));

        string unica = palabras[0];
        return unica.Length >= 3 ? unica[..3] : unica;
    }

    private static string ObtenerInicialesDocumento(string nombre)
    {
        var primeraPalabra = QuitarAcentos(nombre)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant())
            .FirstOrDefault(p => p.Length > 0);

        if (string.IsNullOrEmpty(primeraPalabra)) return "DOC";
        return primeraPalabra.Length >= 3 ? primeraPalabra[..3] : primeraPalabra;
    }

    private static string QuitarAcentos(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        var normalizado = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static string ObtenerMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static string ConstruirRutaCarpeta(int? carpetaId, string fallbackNombre, List<Carpeta> todasCarpetas)
    {
        Carpeta? actual = null;
        if (carpetaId.HasValue)
        {
            actual = todasCarpetas.FirstOrDefault(c => c.Id == carpetaId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(fallbackNombre))
        {
            actual = todasCarpetas.FirstOrDefault(c =>
                c.CarpetaPadreId == null && string.Equals(c.Nombre, fallbackNombre, StringComparison.OrdinalIgnoreCase))
                ?? todasCarpetas.FirstOrDefault(c => string.Equals(c.Nombre, fallbackNombre, StringComparison.OrdinalIgnoreCase));
        }

        if (actual == null)
            return fallbackNombre;

        var segmentos = new List<string>();
        var visitados = new HashSet<int>();
        var cursor = actual;
        while (cursor != null && visitados.Add(cursor.Id))
        {
            segmentos.Add(cursor.Nombre);
            cursor = cursor.CarpetaPadreId.HasValue
                ? todasCarpetas.FirstOrDefault(c => c.Id == cursor.CarpetaPadreId.Value)
                : null;
        }
        segmentos.Reverse();
        return string.Join(" / ", segmentos);
    }

    private static DocumentoResumen MapearAResumen(Documento doc) => new()
    {
        Id = doc.Id,
        Codigo = doc.Codigo,
        Nombre = doc.Nombre,
        Tipo = doc.Tipo.ToString(),
        Departamento = doc.Departamento,
        CarpetaId = doc.CarpetaId,
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
        FechaUltimaModificacion = doc.FechaUltimaModificacion,
        Eliminado = doc.Eliminado,
        FechaEliminacion = doc.FechaEliminacion,
        Etiquetas = SepararEtiquetas(doc.Etiquetas)
    };

    private NotFoundObjectResult NoEncontrado(int id) =>
        NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"Documento con Id {id} no existe." });

    private BadRequestObjectResult Denegado(string detalle) =>
        BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = detalle });

    private BadRequestObjectResult EnPapelera(Documento doc) =>
        BadRequest(new RespuestaError
        {
            Error = "Documento en papelera",
            Detalle = $"El documento '{doc.Codigo}' está en la papelera. Restáurelo antes de bloquearlo o editarlo."
        });

    private BadRequestObjectResult RolInvalido(string? rol) =>
        BadRequest(new RespuestaError
        {
            Error = "Rol inválido",
            Detalle = $"El rol '{rol}' no es válido. Use: Lector, Revisor, Editor o Administrador."
        });

    private ConflictObjectResult Conflicto(Documento doc) =>
        Conflict(new RespuestaError
        {
            Error = "Conflicto de bloqueo",
            Detalle = $"CONFLICTO: El documento ya está bloqueado por '{doc.Bloqueo!.UsuarioBloqueador}' hasta las {doc.Bloqueo.FechaExpiracion:HH:mm:ss dd/MM/yyyy}."
        });
}
