namespace DocControl.Api.Models;

/// <summary>Comentario corporativo por documento y/o versión específica con soporte de menciones @usuario (RF-15).</summary>
public class ComentarioDocumento
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }

    /// <summary>Versión a la que se vincula el comentario (ej. "General" o "v1.2").</summary>
    public string VersionNumero { get; set; } = "General";
    public string Autor { get; set; } = string.Empty;
    public string RolAutor { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
}
