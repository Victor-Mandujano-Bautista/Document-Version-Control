using System.Text.Json.Serialization;

namespace DocControl.Api.Models;

/// <summary>Registro histórico de una versión específica de un documento.</summary>
public class VersionDocumento
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public int NumeroVersion { get; set; }
    public string VersionTexto { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>Nombre del archivo dentro de /Storage. Null en versiones sembradas sin binario. No se expone al cliente.</summary>
    [JsonIgnore]
    public string? RutaArchivo { get; set; }

    /// <summary>Indica al frontend si la versión tiene archivo descargable.</summary>
    public bool TieneArchivo => !string.IsNullOrEmpty(RutaArchivo);

    public string Autor { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public string HashSHA256 { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public long TamanoBytes { get; set; }
}
