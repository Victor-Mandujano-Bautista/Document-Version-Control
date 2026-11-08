namespace DocControl.Api.Models;

/// <summary>Tipos de documento empresarial soportados.</summary>
public enum TipoDocumento
{
    Word,
    Excel,
    PowerPoint,
    PDF
}

/// <summary>Estado del documento en el flujo de trabajo (bloqueo de edición).</summary>
public enum EstadoDocumento
{
    Disponible,
    Bloqueado
}

/// <summary>Estado del flujo de revisión/dictamen del documento.</summary>
public enum EstadoRevision
{
    Aprobado,
    EnRevision,
    Rechazado
}

/// <summary>Roles del sistema RBAC.</summary>
public enum RolUsuario
{
    Lector,
    Revisor,
    Editor,
    Administrador
}

/// <summary>Acciones registradas en la bitácora de auditoría.</summary>
public enum AccionBitacora
{
    BLOQUEO_EDICION,
    EXTENSION_TIEMPO,
    NUEVA_VERSION,
    DESBLOQUEO_FORZADO,
    ALTA_DOCUMENTO,
    BAJA_DOCUMENTO,
    SOLICITUD_REVISION,
    REVISION_APROBADA,
    REVISION_RECHAZADA,
    EXPIRACION_AUTOMATICA
}
