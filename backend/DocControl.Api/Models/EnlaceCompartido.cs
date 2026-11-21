namespace DocControl.Api.Models;

/// <summary>Enlace compartido temporal de solo lectura con expiración y revocación (RF-20).</summary>
public class EnlaceCompartido
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int DocumentoId { get; set; }
    public int? VersionId { get; set; }
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaExpiracion { get; set; }
    public bool Revocado { get; set; } = false;
    public DateTime? FechaRevocacion { get; set; }
    public bool PermiteDescarga { get; set; } = true;
    public int TotalAccesos { get; set; } = 0;
}
