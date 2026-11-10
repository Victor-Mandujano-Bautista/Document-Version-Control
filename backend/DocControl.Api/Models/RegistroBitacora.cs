namespace DocControl.Api.Models;

/// <summary>Registro inmutable de la bitácora digital de auditoría.</summary>
public class RegistroBitacora
{
    public int Folio { get; set; }
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    public string CodigoDocumento { get; set; } = string.Empty;
    public string NombreDocumento { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public AccionBitacora Accion { get; set; }
    public string Observaciones { get; set; } = string.Empty;

    /// <summary>IP y User-Agent capturados automáticamente de la petición HTTP.</summary>
    public string DatosMaquina { get; set; } = string.Empty;
}

