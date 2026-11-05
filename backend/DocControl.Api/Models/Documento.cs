namespace DocControl.Api.Models;

/// <summary>Entidad principal del documento empresarial.</summary>
public class Documento
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; }
    public string Departamento { get; set; } = string.Empty;
    public string VersionActual { get; set; } = "v1.0";
    public int NumeroVersionActual { get; set; } = 1;
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Disponible;
    public InfoBloqueo? Bloqueo { get; set; }
    public List<VersionDocumento> HistorialVersiones { get; set; } = new();
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaUltimaModificacion { get; set; }
}

