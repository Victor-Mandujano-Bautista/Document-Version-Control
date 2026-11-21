namespace DocControl.Api.Models;

/// <summary>Permiso de acceso configurado por rol sobre una carpeta o subcarpeta (RF-12).</summary>
public class PermisoCarpeta
{
    public int Id { get; set; }
    public int CarpetaId { get; set; }

    /// <summary>Rol: "Administrador", "Revisor", "Editor" o "Lector".</summary>
    public string Rol { get; set; } = string.Empty;

    /// <summary>Nivel de acceso: "SinAcceso", "Lectura", "Edicion" o "Administracion".</summary>
    public string NivelAcceso { get; set; } = "Lectura";

    public string ActualizadoPor { get; set; } = string.Empty;
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
