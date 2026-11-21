namespace DocControl.Api.Models;

/// <summary>Parámetro global de configuración del sistema persistido en SQL Server (ej. retención de papelera RF-10).</summary>
public class ConfiguracionSistema
{
    public int Id { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string ActualizadoPor { get; set; } = "Sistema";
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
