namespace DocControl.Api.Models;

/// <summary>Registro histórico de una versión específica de un documento.</summary>
public class VersionDocumento
{
    public int NumeroVersion { get; set; }
    public string VersionTexto { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string Autor { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public string HashSHA256 { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public long TamanoBytes { get; set; }
}

