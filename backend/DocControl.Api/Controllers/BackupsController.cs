using DocControl.Api.Data;
using DocControl.Api.Models;
using DocControl.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Controllers;

[ApiController]
[Route("api/backups")]
public class BackupsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly BackupService _backupService;
    private readonly IConfiguration _config;

    public BackupsController(AppDbContext context, BackupService backupService, IConfiguration config)
    {
        _context = context;
        _backupService = backupService;
        _config = config;
    }

    /// <summary>Listar todos los respaldos y el estado de programación automática (Exclusivo Administrador - RF-21).</summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerRespaldos([FromQuery] string? rol = null, [FromQuery] string? usuario = null)
    {
        string? rolEfectivo = !string.IsNullOrWhiteSpace(rol) ? rol : Request.Headers["X-Rol"].FirstOrDefault();
        string? usuarioEfectivo = !string.IsNullOrWhiteSpace(usuario) ? usuario : Request.Headers["X-Usuario"].FirstOrDefault();

        var validacion = await ValidarAdministradorAsync(usuarioEfectivo, rolEfectivo, "consultar el historial de respaldos del sistema");
        if (validacion != null) return validacion;

        var respaldos = await _context.RegistrosRespaldo
            .AsNoTracking()
            .OrderByDescending(r => r.FechaCreacion)
            .ThenByDescending(r => r.Id)
            .ToListAsync();

        bool habilitado = _config.GetValue<bool?>("BackupSettings:Enabled") ?? true;
        int intervaloHoras = Math.Max(1, _config.GetValue<int?>("BackupSettings:IntervalHours") ?? 24);
        int retencionMaxima = Math.Max(1, _config.GetValue<int?>("BackupSettings:RetentionCount") ?? 10);
        long espacioTotalBytes = respaldos.Where(r => r.Estado == "Completado").Sum(r => r.TamanoBytes);

        return Ok(new
        {
            habilitadoAutomatico = habilitado,
            intervaloHoras,
            retencionMaxima,
            proximoRespaldoUtc = BackupScheduleState.NextScheduledRunUtc ?? DateTime.UtcNow.AddHours(intervaloHoras),
            espacioTotalBytes,
            respaldos
        });
    }

    /// <summary>Generar un nuevo respaldo completo (.zip) manual de forma inmediata (Exclusivo Administrador - RF-21).</summary>
    [HttpPost]
    public async Task<IActionResult> CrearRespaldoManual([FromBody] SolicitudUsuario solicitud)
    {
        var validacion = await ValidarAdministradorAsync(solicitud?.Usuario, solicitud?.Rol, "crear respaldos manuales del sistema");
        if (validacion != null) return validacion;

        try
        {
            var registro = await _backupService.CreateBackupAsync(
                "Manual",
                solicitud!.Usuario,
                ObtenerDatosMaquina());

            return Ok(new
            {
                mensaje = $"Respaldo '{registro.NombreArchivo}' generado exitosamente.",
                respaldo = registro
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new RespuestaError
            {
                Error = "Error al crear respaldo",
                Detalle = ex.Message
            });
        }
    }

    /// <summary>Descargar el archivo .zip de un respaldo específico (Exclusivo Administrador - RF-21).</summary>
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> DescargarRespaldo(int id, [FromQuery] string? rol = null, [FromQuery] string? usuario = null)
    {
        string? rolEfectivo = !string.IsNullOrWhiteSpace(rol) ? rol : Request.Headers["X-Rol"].FirstOrDefault();
        string? usuarioEfectivo = !string.IsNullOrWhiteSpace(usuario) ? usuario : Request.Headers["X-Usuario"].FirstOrDefault();

        var validacion = await ValidarAdministradorAsync(usuarioEfectivo, rolEfectivo, $"descargar el archivo de respaldo #{id}");
        if (validacion != null) return validacion;

        var registro = await _context.RegistrosRespaldo.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (registro == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"El respaldo con Id {id} no existe." });

        string? rutaZip = _backupService.ResolverRutaZip(registro);
        if (rutaZip == null || !System.IO.File.Exists(rutaZip))
            return NotFound(new RespuestaError { Error = "Archivo no disponible", Detalle = $"El archivo físico '{registro.NombreArchivo}' no se encuentra en el servidor." });

        var stream = new FileStream(rutaZip, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/zip", registro.NombreArchivo);
    }

    /// <summary>Restaurar completamente SQL Server y /Storage a partir de un respaldo (.zip) (Exclusivo Administrador - RF-21).</summary>
    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> RestaurarRespaldo(int id, [FromBody] SolicitudUsuario solicitud)
    {
        var validacion = await ValidarAdministradorAsync(solicitud?.Usuario, solicitud?.Rol, $"restaurar el sistema desde el respaldo #{id}");
        if (validacion != null) return validacion;

        try
        {
            var restaurado = await _backupService.RestoreBackupAsync(id, solicitud!.Usuario, ObtenerDatosMaquina());
            return Ok(new
            {
                mensaje = $"Sistema restaurado exitosamente al punto de control '{restaurado.NombreArchivo}'.",
                respaldo = restaurado,
                resumen = new
                {
                    restaurado.TotalCarpetas,
                    restaurado.TotalDocumentos,
                    restaurado.TotalVersiones,
                    restaurado.TotalArchivosFisicos
                }
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new RespuestaError { Error = "Archivo no disponible", Detalle = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new RespuestaError
            {
                Error = "Error en la restauración",
                Detalle = ex.Message
            });
        }
    }

    /// <summary>Eliminar un respaldo del historial y su archivo .zip en disco (Exclusivo Administrador - RF-21).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> EliminarRespaldo(int id, [FromQuery] string? usuario = null, [FromQuery] string? rol = null)
    {
        string? rolEfectivo = !string.IsNullOrWhiteSpace(rol) ? rol : Request.Headers["X-Rol"].FirstOrDefault();
        string? usuarioEfectivo = !string.IsNullOrWhiteSpace(usuario) ? usuario : Request.Headers["X-Usuario"].FirstOrDefault();

        var validacion = await ValidarAdministradorAsync(usuarioEfectivo, rolEfectivo, $"eliminar el respaldo #{id}");
        if (validacion != null) return validacion;

        var registro = await _context.RegistrosRespaldo.FirstOrDefaultAsync(r => r.Id == id);
        if (registro == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"El respaldo con Id {id} no existe." });

        string nombreArchivo = registro.NombreArchivo;
        string actor = string.IsNullOrWhiteSpace(usuarioEfectivo) ? "Elena Martínez" : usuarioEfectivo.Trim();

        _backupService.BorrarArchivoZipFisico(registro);
        _context.RegistrosRespaldo.Remove(registro);

        _context.Bitacora.Add(new RegistroBitacora
        {
            FechaHora = DateTime.UtcNow,
            Usuario = actor,
            Rol = RolUsuario.Administrador,
            CodigoDocumento = "BACKUP",
            NombreDocumento = nombreArchivo,
            Version = "-",
            Accion = AccionBitacora.ELIMINAR_RESPALDO,
            Observaciones = $"Respaldo '{nombreArchivo}' ({registro.Tipo}) eliminado del servidor y del catálogo de respaldos.",
            DatosMaquina = ObtenerDatosMaquina()
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = $"Respaldo '{nombreArchivo}' eliminado correctamente.",
            eliminado = true
        });
    }

    private async Task<IActionResult?> ValidarAdministradorAsync(string? usuario, string? rolTexto, string accionIntentada)
    {
        if (!Enum.TryParse<RolUsuario>(rolTexto ?? string.Empty, true, out var rol) || !Enum.IsDefined(rol))
        {
            return BadRequest(new RespuestaError
            {
                Error = "Rol inválido",
                Detalle = $"El rol '{rolTexto}' no es válido. Use: Lector, Revisor, Editor o Administrador."
            });
        }

        if (rol == RolUsuario.Administrador)
            return null;

        string actor = string.IsNullOrWhiteSpace(usuario) ? "Desconocido" : usuario.Trim();
        _context.Bitacora.Add(new RegistroBitacora
        {
            FechaHora = DateTime.UtcNow,
            Usuario = actor,
            Rol = rol,
            CodigoDocumento = "BACKUP",
            NombreDocumento = "Módulo de Respaldos",
            Version = "-",
            Accion = AccionBitacora.ACCESO_DENEGADO,
            Observaciones = $"Intento de acción sin permiso: {accionIntentada}. El rol '{rol}' no tiene acceso al módulo de respaldos.",
            DatosMaquina = ObtenerDatosMaquina()
        });
        await _context.SaveChangesAsync();

        return StatusCode(StatusCodes.Status403Forbidden, new RespuestaError
        {
            Error = "Acceso denegado",
            Detalle = $"El rol '{rol}' no tiene permisos para {accionIntentada} (exclusivo para Administrador)."
        });
    }

    private string ObtenerDatosMaquina()
    {
        var ip = HttpContext?.Connection.RemoteIpAddress;
        string ipTexto = ip == null
            ? "desconocida"
            : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString());
        string ua = Request?.Headers.UserAgent.ToString() ?? "no informado";
        if (ua.Length > 350) ua = ua[..350];
        return $"IP: {ipTexto} | User-Agent: {ua}";
    }
}
