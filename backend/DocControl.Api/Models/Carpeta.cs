namespace DocControl.Api.Models;

/// <summary>Entidad de Carpeta (Folder) departamental con color personalizado y metadatos de auditoría.</summary>
public class Carpeta
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    /// <summary>Color asignado: "Rojo", "Amarillo", "Azul", "Verde", "Morado" o "Cian".</summary>
    public string Color { get; set; } = "Azul";

    /// <summary>Id de la carpeta padre (null si es carpeta raíz).</summary>
    public int? CarpetaPadreId { get; set; }
}

