using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private const string ClaveRetencionPapelera = "PapeleraDiasRetencion";
    private readonly AppDbContext _context;

    public SettingsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Consultar el plazo de retención automática de la papelera de reciclaje (RF-10).</summary>
    [HttpGet("recycle-bin")]
    public async Task<IActionResult> ObtenerRetencionPapelera()
    {
        var config = await _context.ConfiguracionesSistema
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Clave == ClaveRetencionPapelera);

        int dias = 30;
        if (config != null && int.TryParse(config.Valor, out int parsed) && parsed >= 1 && parsed <= 365)
        {
            dias = parsed;
        }

        return Ok(new
        {
            diasRetencion = dias,
            actualizadoPor = config?.ActualizadoPor ?? "Sistema",
            fechaActualizacion = config?.FechaActualizacion ?? DateTime.UtcNow
        });
    }

    /// <summary>Actualizar el plazo de retención automática de la papelera (Exclusivo para Administrador - RF-10).</summary>
    [HttpPut("recycle-bin")]
    public async Task<IActionResult> ActualizarRetencionPapelera([FromBody] SolicitudActualizarRetencionPapelera solicitud)
    {
        if (!TryParseRol(solicitud.Rol, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido." });

        string actor = string.IsNullOrWhiteSpace(solicitud.Usuario) ? "Desconocido" : solicitud.Usuario.Trim();

        if (rol != RolUsuario.Administrador)
        {
            _context.Bitacora.Add(new RegistroBitacora
            {
                FechaHora = DateTime.UtcNow,
                Usuario = actor,
                Rol = rol,
                CodigoDocumento = "CONFIG",
                NombreDocumento = "Retención de Papelera",
                Version = "-",
                Accion = AccionBitacora.ACCESO_DENEGADO,
                Observaciones = $"Intento sin permiso de modificar la retención de la papelera a {solicitud.DiasRetencion} días. Rol actual: {rol}.",
                DatosMaquina = ObtenerDatosMaquina()
            });
            await _context.SaveChangesAsync();

            return StatusCode(StatusCodes.Status403Forbidden, new RespuestaError
            {
                Error = "Acceso denegado",
                Detalle = "Solo el Administrador puede modificar el plazo de retención de la papelera."
            });
        }

        if (solicitud.DiasRetencion < 1 || solicitud.DiasRetencion > 365)
        {
            return BadRequest(new RespuestaError
            {
                Error = "Plazo inválido",
                Detalle = "El plazo de retención de la papelera debe estar entre 1 y 365 días."
            });
        }

        var config = await _context.ConfiguracionesSistema
            .FirstOrDefaultAsync(c => c.Clave == ClaveRetencionPapelera);

        string valorAnterior = config?.Valor ?? "30";
        var ahora = DateTime.UtcNow;

        if (config == null)
        {
            config = new ConfiguracionSistema
            {
                Clave = ClaveRetencionPapelera,
                Valor = solicitud.DiasRetencion.ToString(),
                ActualizadoPor = actor,
                FechaActualizacion = ahora
            };
            _context.ConfiguracionesSistema.Add(config);
        }
        else
        {
            config.Valor = solicitud.DiasRetencion.ToString();
            config.ActualizadoPor = actor;
            config.FechaActualizacion = ahora;
        }

        _context.Bitacora.Add(new RegistroBitacora
        {
            FechaHora = ahora,
            Usuario = actor,
            Rol = rol,
            CodigoDocumento = "CONFIG",
            NombreDocumento = "Retención de Papelera",
            Version = "-",
            Accion = AccionBitacora.ACTUALIZAR_CONFIGURACION,
            Observaciones = $"Plazo de purga automática de la papelera actualizado: {valorAnterior} días → {solicitud.DiasRetencion} días.",
            DatosMaquina = ObtenerDatosMaquina()
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = $"Plazo de retención de la papelera actualizado a {solicitud.DiasRetencion} días.",
            diasRetencion = solicitud.DiasRetencion,
            actualizadoPor = config.ActualizadoPor,
            fechaActualizacion = config.FechaActualizacion
        });
    }

    private static bool TryParseRol(string? valor, out RolUsuario rol) =>
        Enum.TryParse(valor ?? string.Empty, true, out rol) && Enum.IsDefined(rol);

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
