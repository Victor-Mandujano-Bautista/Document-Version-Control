using DocControl.Api.Data;
using DocControl.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Obtener las notificaciones exclusivas del usuario/rol activo (RF-17).</summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerNotificaciones([FromQuery] string? usuario, [FromQuery] string? rol)
    {
        if (string.IsNullOrWhiteSpace(usuario) && string.IsNullOrWhiteSpace(rol))
        {
            return Ok(new
            {
                noLeidas = 0,
                notificaciones = Array.Empty<object>()
            });
        }

        var recientes = await _context.Notificaciones
            .AsNoTracking()
            .OrderByDescending(n => n.FechaCreacion)
            .ThenByDescending(n => n.Id)
            .Take(200)
            .ToListAsync();

        var filtradas = recientes
            .Where(n => NotificationService.CoincideConSesion(n.Destinatario, n.RolDestinatario, usuario, rol))
            .Take(40)
            .ToList();

        int noLeidas = filtradas.Count(n => !n.Leida);

        return Ok(new
        {
            noLeidas,
            notificaciones = filtradas
        });
    }

    /// <summary>Marcar una notificación individual como leída.</summary>
    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarcarComoLeida(int id)
    {
        var notif = await _context.Notificaciones.FirstOrDefaultAsync(n => n.Id == id);
        if (notif == null)
            return NotFound(new { error = "No encontrado", detalle = $"La notificación {id} no existe." });

        if (!notif.Leida)
        {
            notif.Leida = true;
            await _context.SaveChangesAsync();
        }

        return Ok(new { mensaje = "Notificación marcada como leída." });
    }

    /// <summary>Marcar todas las notificaciones no leídas del usuario/rol activo como leídas.</summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> MarcarTodasComoLeidas([FromQuery] string? usuario, [FromQuery] string? rol)
    {
        if (string.IsNullOrWhiteSpace(usuario) && string.IsNullOrWhiteSpace(rol))
        {
            return Ok(new
            {
                mensaje = "Sin usuario activo especificado.",
                actualizadas = 0
            });
        }

        var noLeidas = await _context.Notificaciones
            .Where(n => !n.Leida)
            .ToListAsync();

        int actualizadas = 0;
        foreach (var n in noLeidas)
        {
            if (NotificationService.CoincideConSesion(n.Destinatario, n.RolDestinatario, usuario, rol))
            {
                n.Leida = true;
                actualizadas++;
            }
        }

        if (actualizadas > 0)
            await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = "Notificaciones marcadas como leídas.",
            actualizadas
        });
    }

    /// <summary>Eliminar una notificación individual verificando estrictamente la pertenencia al usuario/rol activo.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> EliminarNotificacion(int id, [FromQuery] string? usuario, [FromQuery] string? rol)
    {
        if (string.IsNullOrWhiteSpace(usuario) && string.IsNullOrWhiteSpace(rol))
            return NotFound(new { error = "No encontrado", detalle = "La notificación no existe o no pertenece al usuario activo." });

        var notif = await _context.Notificaciones.FirstOrDefaultAsync(n => n.Id == id);
        if (notif == null || !NotificationService.CoincideConSesion(notif.Destinatario, notif.RolDestinatario, usuario, rol))
            return NotFound(new { error = "No encontrado", detalle = "La notificación no existe o no pertenece al usuario activo." });

        _context.Notificaciones.Remove(notif);
        await _context.SaveChangesAsync();

        return Ok(new { eliminada = true });
    }

    /// <summary>Eliminar todas las notificaciones que pertenecen estrictamente al usuario/rol activo.</summary>
    [HttpDelete("clear")]
    public async Task<IActionResult> LimpiarNotificaciones([FromQuery] string? usuario, [FromQuery] string? rol)
    {
        if (string.IsNullOrWhiteSpace(usuario) && string.IsNullOrWhiteSpace(rol))
            return BadRequest(new { error = "Parámetros inválidos", detalle = "Debe especificar el usuario o rol activo para borrar sus notificaciones." });

        var todas = await _context.Notificaciones.ToListAsync();
        var delUsuario = todas
            .Where(n => NotificationService.CoincideConSesion(n.Destinatario, n.RolDestinatario, usuario, rol))
            .ToList();

        if (delUsuario.Count > 0)
        {
            _context.Notificaciones.RemoveRange(delUsuario);
            await _context.SaveChangesAsync();
        }

        return Ok(new { eliminadas = delUsuario.Count });
    }
}
