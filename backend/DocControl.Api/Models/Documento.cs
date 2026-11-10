namespace DocControl.Api.Models;

/// <summary>Entidad principal del documento empresarial.</summary>
public class Documento
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; }
    public string Departamento { get; set; } = string.Empty;

    /// <summary>Vínculo directo con la Carpeta o Subcarpeta (RF-04). Mantiene retrocompatibilidad con Departamento.</summary>
    public int? CarpetaId { get; set; }
    public string VersionActual { get; set; } = "v1.0";
    public int NumeroVersionActual { get; set; } = 1;
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Disponible;

    /// <summary>Estado del flujo de revisión (Aprobado / EnRevision / Rechazado).</summary>
    public EstadoRevision EstadoRevision { get; set; } = EstadoRevision.Aprobado;
    public string? RevisadoPor { get; set; }
    public string? ComentarioRevision { get; set; }
    public InfoBloqueo? Bloqueo { get; set; }
    public List<VersionDocumento> HistorialVersiones { get; set; } = new();
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaUltimaModificacion { get; set; }

    /// <summary>Papelera (RF-10): borrado lógico. Un documento en papelera no se puede bloquear ni editar.</summary>
    public bool Eliminado { get; set; } = false;
    public DateTime? FechaEliminacion { get; set; }

    /// <summary>Etiquetas separadas por coma (RF-14). Ej. "Urgente,Oficial".</summary>
    public string Etiquetas { get; set; } = string.Empty;

    /// <summary>Token de concurrencia optimista: evita que dos bloqueos simultáneos ganen a la vez.</summary>
    public byte[]? RowVersion { get; set; }
}
