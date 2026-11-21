namespace DocControl.Api.Models;

/// <summary>Registro histórico de un respaldo (.zip) de base de datos y archivos físicos (RF-21).</summary>
public class RegistroRespaldo
{
    public int Id { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string RutaArchivo { get; set; } = string.Empty;
    /// <summary>"Manual" | "Automatico"</summary>
    public string Tipo { get; set; } = "Manual";
    /// <summary>"Completado" | "Error"</summary>
    public string Estado { get; set; } = "Completado";
    public long TamanoBytes { get; set; }
    public int TotalCarpetas { get; set; }
    public int TotalDocumentos { get; set; }
    public int TotalVersiones { get; set; }
    public int TotalArchivosFisicos { get; set; }
    public string HashIntegridad { get; set; } = string.Empty;
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaUltimaRestauracion { get; set; }
    public string? DetalleError { get; set; }
}
