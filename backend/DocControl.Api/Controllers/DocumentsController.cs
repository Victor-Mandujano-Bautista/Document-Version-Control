using DocControl.Api.Models;
using DocControl.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DocControl.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentWorkspaceService _workspace;

    public DocumentsController(DocumentWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    // ═══════════════════════════════════════════════════════════
    //  CONSULTAS
    // ═══════════════════════════════════════════════════════════

    /// <summary>Obtener todos los documentos con su estado actual.</summary>
    [HttpGet]
    public IActionResult ObtenerDocumentos()
    {
        return Ok(_workspace.ObtenerTodosLosDocumentos());
    }

    /// <summary>Obtener KPIs de resumen del workspace.</summary>
    [HttpGet("kpis")]
    public IActionResult ObtenerKpis()
    {
        return Ok(_workspace.ObtenerKpis());
    }

    /// <summary>Obtener historial de versiones de un documento.</summary>
    [HttpGet("{id}/historial")]
    public IActionResult ObtenerHistorial(int id)
    {
        var doc = _workspace.ObtenerDocumentoPorId(id);
        if (doc == null)
            return NotFound(new RespuestaError { Error = "No encontrado", Detalle = $"Documento con Id {id} no existe." });

        return Ok(_workspace.ObtenerHistorial(id));
    }

    /// <summary>Obtener la bitácora digital de auditoría.</summary>
    [HttpGet("bitacora")]
    public IActionResult ObtenerBitacora()
    {
        return Ok(_workspace.ObtenerBitacora());
    }

    // ═══════════════════════════════════════════════════════════
    //  BLOQUEO (CHECK-OUT)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Bloquear un documento para edición exclusiva (Check-out).</summary>
    [HttpPost("{id}/lock")]
    public IActionResult BloquearDocumento(int id, [FromBody] SolicitudBloqueo solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido. Use: Lector, Editor o Administrador." });

        var (exito, mensaje) = _workspace.BloquearDocumento(id, solicitud.Usuario, rol);

        if (!exito)
        {
            if (mensaje.Contains("CONFLICTO"))
                return Conflict(new RespuestaError { Error = "Conflicto de bloqueo", Detalle = mensaje });
            return BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = mensaje });
        }

        return Ok(new { mensaje });
    }

    // ═══════════════════════════════════════════════════════════
    //  EXTENSIÓN DE TIEMPO
    // ═══════════════════════════════════════════════════════════

    /// <summary>Extender el tiempo de bloqueo activo en +15 minutos.</summary>
    [HttpPost("{id}/extend-lock")]
    public IActionResult ExtenderBloqueo(int id, [FromBody] SolicitudExtension solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido." });

        var (exito, mensaje) = _workspace.ExtenderBloqueo(id, solicitud.Usuario, rol);

        if (!exito)
            return BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = mensaje });

        return Ok(new { mensaje });
    }

    // ═══════════════════════════════════════════════════════════
    //  CHECK-IN (NUEVA VERSIÓN)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Subir una nueva versión del documento (Check-in).</summary>
    [HttpPost("{id}/checkin")]
    public IActionResult Checkin(int id, [FromBody] SolicitudCheckin solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido." });

        var (exito, mensaje) = _workspace.Checkin(id, solicitud.Usuario, rol,
            solicitud.NombreArchivo, solicitud.Comentario, solicitud.VersionMayor);

        if (!exito)
            return BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = mensaje });

        return Ok(new { mensaje });
    }

    // ═══════════════════════════════════════════════════════════
    //  DESBLOQUEO FORZADO (ADMIN)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Desbloqueo administrativo forzado con motivo obligatorio.</summary>
    [HttpPost("{id}/force-unlock")]
    public IActionResult DesbloqueoForzado(int id, [FromBody] SolicitudDesbloqueoForzado solicitud)
    {
        if (!Enum.TryParse<RolUsuario>(solicitud.Rol, true, out var rol))
            return BadRequest(new RespuestaError { Error = "Rol inválido", Detalle = $"El rol '{solicitud.Rol}' no es válido." });

        var (exito, mensaje) = _workspace.DesbloqueoForzado(id, solicitud.Usuario, rol, solicitud.Motivo);

        if (!exito)
            return BadRequest(new RespuestaError { Error = "Operación denegada", Detalle = mensaje });

        return Ok(new { mensaje });
    }
}

