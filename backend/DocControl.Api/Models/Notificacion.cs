namespace DocControl.Api.Models;

/// <summary>Notificación interna del centro de notificaciones (RF-17).</summary>
public class Notificacion
{
    public int Id { get; set; }
    public string Destinatario { get; set; } = string.Empty;
    public string RolDestinatario { get; set; } = string.Empty;

    /// <summary>Valores: "MENCION", "COMENTARIO", "SOLICITUD_REVISION", "REVISION_DICTAMINADA", "ARCHIVO_LIBERADO".</summary>
    public string Tipo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int? DocumentoId { get; set; }
    public string? CodigoDocumento { get; set; }
    public bool Leida { get; set; } = false;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
