namespace DocControl.Api.Models;

/// <summary>Tipos de documento empresarial soportados.</summary>
public enum TipoDocumento
{
    Word,
    Excel,
    PowerPoint,
    PDF
}

/// <summary>Estado del documento en el flujo de trabajo.</summary>
public enum EstadoDocumento
{
    Disponible,
    Bloqueado
}

/// <summary>Roles del sistema RBAC.</summary>
public enum RolUsuario
{
    Lector,
    Editor,
    Administrador
}

/// <summary>Acciones registradas en la bitácora de auditoría.</summary>
public enum AccionBitacora
{
    BLOQUEO_EDICION,
    EXTENSION_TIEMPO,
    NUEVA_VERSION,
    DESBLOQUEO_FORZADO
}

