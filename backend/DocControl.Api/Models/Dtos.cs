namespace DocControl.Api.Models;

// ── DTOs de Solicitud ──

public class SolicitudBloqueo
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public class SolicitudExtension
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public class SolicitudCheckin
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public bool VersionMayor { get; set; }
}

public class SolicitudRevision
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public class SolicitudDictamen
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Aprobar { get; set; }
    public string Comentario { get; set; } = string.Empty;
}

public class SolicitudDesbloqueoForzado
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>Solicitud genérica con solo la identidad simulada (ej. restaurar de papelera).</summary>
public class SolicitudUsuario
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

/// <summary>Restaurar una versión anterior como nueva versión vigente (RF-07).</summary>
public class SolicitudRestaurarVersion
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    /// <summary>Opcional: ¿por qué se restaura?</summary>
    public string? Motivo { get; set; }
}

/// <summary>Edición de metadatos (RF-03, RF-04, RF-14). Los campos null no se modifican.</summary>
public class SolicitudMetadatos
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string? Departamento { get; set; }
    /// <summary>Etiquetas separadas por coma. Cadena vacía = quitar todas.</summary>
    public string? Etiquetas { get; set; }
}

/// <summary>Crear un nuevo folder departamental o subcarpeta (Solo Administrador o Revisor).</summary>
public class SolicitudCrearCarpeta
{
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = "Azul";
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public int? CarpetaPadreId { get; set; }
}

/// <summary>Actualizar nombre y/o color de un folder (PROHIBIDO modificar CreadoPor o FechaCreacion).</summary>
public class SolicitudActualizarCarpeta
{
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = "Azul";
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

/// <summary>Mover un documento entre carpetas o subcarpetas (Administrador, Revisor y Editor).</summary>
public class SolicitudMoverDocumento
{
    public int? CarpetaDestinoId { get; set; }
    public string CarpetaDestino { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

// ── DTOs de Respuesta ──

public class CarpetaResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public string Color { get; set; } = string.Empty;
    public int? CarpetaPadreId { get; set; }
    public int TotalArchivos { get; set; }
    public int TotalSubcarpetas { get; set; }
    public int TotalDocumentos { get; set; }
}

public class DocumentoResumen
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public int? CarpetaId { get; set; }
    public string VersionActual { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string EstadoRevision { get; set; } = string.Empty;
    public string? RevisadoPor { get; set; }
    public string? ComentarioRevision { get; set; }
    public BloqueoResumen? Bloqueo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaUltimaModificacion { get; set; }
    public bool Eliminado { get; set; }
    public DateTime? FechaEliminacion { get; set; }
    public List<string> Etiquetas { get; set; } = new();
}

public class BloqueoResumen
{
    public string Usuario { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public DateTime FechaBloqueo { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public bool Expirado { get; set; }
}

public class ResumenKpis
{
    public int TotalDocumentos { get; set; }
    public int DocumentosDisponibles { get; set; }
    public int DocumentosBloqueados { get; set; }
    public int RegistrosBitacora { get; set; }
    public int DocumentosEnPapelera { get; set; }
}

public class RespuestaError
{
    public string Error { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}

