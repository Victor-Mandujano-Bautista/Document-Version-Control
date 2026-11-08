namespace DocControl.Api.Models;

// ── DTOs de Solicitud ──

public class SolicitudBloqueo
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public class SolicitudExtension
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public class SolicitudCheckin
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public bool VersionMayor { get; set; }
}

public class SolicitudRevision
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public class SolicitudDictamen
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Aprobar { get; set; }
    public string Comentario { get; set; } = string.Empty;
}

public class SolicitudDesbloqueoForzado
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
}

// ── DTOs de Respuesta ──

public class DocumentoResumen
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public string VersionActual { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string EstadoRevision { get; set; } = string.Empty;
    public string? RevisadoPor { get; set; }
    public string? ComentarioRevision { get; set; }
    public BloqueoResumen? Bloqueo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaUltimaModificacion { get; set; }
}

public class BloqueoResumen
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public DateTime FechaBloqueo { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public bool Expirado { get; set; }
}

public class ResumenKpis
{
    public int TotalDocumentos { get; set; }
    public int DocumentosDisponibles { get; set; }
    public int DocumentosBloqueados { get; set; }
    public int RegistrosBitacora { get; set; }
}

public class RespuestaError
{
    public string Error { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}

