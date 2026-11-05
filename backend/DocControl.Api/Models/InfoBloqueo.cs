namespace DocControl.Api.Models;

/// <summary>Información del bloqueo activo de un documento.</summary>
public class InfoBloqueo
{
    public string UsuarioBloqueador { get; set; } = string.Empty;
    public RolUsuario RolBloqueador { get; set; }
    public DateTime FechaBloqueo { get; set; }
    public DateTime FechaExpiracion { get; set; }

    public bool EstaExpirado => DateTime.UtcNow > FechaExpiracion;
}

