namespace DocControl.Api.Models;

/// <summary>Tipos de documento empresarial soportados.</summary>
public enum TipoDocumento
{
    Word,
    Excel,
    PowerPoint,
    PDF,
    Imagen,
    Texto
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
    EXPIRACION_AUTOMATICA,
    RESTAURAR_VERSION,
    RESTAURAR_PAPELERA,
    ELIMINACION_DEFINITIVA,
    RENOMBRAR_DOCUMENTO,
    DESCARGA_ARCHIVO,
    ACCESO_DENEGADO,
    ACTUALIZACION_METADATOS,
    MOVIMIENTO_CARPETA,
    ALTA_CARPETA,
    MODIFICACION_CARPETA,
    ACTUALIZAR_ETIQUETAS,
    AGREGAR_COMENTARIO,
    CREAR_ENLACE_TEMPORAL,
    REVOCAR_ENLACE_TEMPORAL,
    ACCESO_ENLACE_EXTERNO
}
