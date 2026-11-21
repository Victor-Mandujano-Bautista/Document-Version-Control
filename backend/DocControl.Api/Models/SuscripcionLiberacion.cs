namespace DocControl.Api.Models;

/// <summary>Lista de espera de bloqueo ("Avisarme cuando esté libre" - RF-17).</summary>
public class SuscripcionLiberacion
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
    public bool Notificado { get; set; } = false;
}
