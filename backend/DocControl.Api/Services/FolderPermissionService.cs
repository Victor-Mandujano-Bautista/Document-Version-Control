using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Services;

/// <summary>
/// Servicio de resolución y validación de permisos por carpeta y rol con soporte de herencia jerárquica (RF-12).
/// </summary>
public class FolderPermissionService
{
    public const string SinAcceso = "SinAcceso";
    public const string Lectura = "Lectura";
    public const string Edicion = "Edicion";
    public const string Administracion = "Administracion";

    public static readonly string[] NivelesValidos = [SinAcceso, Lectura, Edicion, Administracion];
    public static readonly string[] RolesSistema = ["Administrador", "Revisor", "Editor", "Lector"];

    private readonly AppDbContext _context;

    public FolderPermissionService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Normaliza el nombre del nivel de acceso a uno de los 4 valores canónicos, o null si no es válido.</summary>
    public static string? NormalizarNivelAcceso(string? nivel)
    {
        if (string.IsNullOrWhiteSpace(nivel)) return null;
        var limpio = nivel.Trim();
        return NivelesValidos.FirstOrDefault(n => string.Equals(n, limpio, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Devuelve el permiso base por defecto de un rol cuando ninguna carpeta de la jerarquía tiene regla explícita.</summary>
    public static string ObtenerNivelPorDefecto(string? rol)
    {
        if (string.Equals(rol, "Administrador", StringComparison.OrdinalIgnoreCase))
            return Administracion;
        if (string.Equals(rol, "Editor", StringComparison.OrdinalIgnoreCase))
            return Edicion;
        if (string.Equals(rol, "Revisor", StringComparison.OrdinalIgnoreCase))
            return Lectura;
        return Lectura; // Lector
    }

    /// <summary>Jerarquía numérica de capacidades: SinAcceso (0) &lt; Lectura (1) &lt; Edicion (2) &lt; Administracion (3).</summary>
    public static int ObtenerRangoNivel(string? nivel) => nivel switch
    {
        SinAcceso => 0,
        Lectura => 1,
        Edicion => 2,
        Administracion => 3,
        _ => 1
    };

    public static bool CumpleNivelMinimo(string? nivelEfectivo, string nivelMinimoRequerido) =>
        ObtenerRangoNivel(nivelEfectivo) >= ObtenerRangoNivel(nivelMinimoRequerido);

    /// <summary>
    /// Resuelve en memoria el nivel de acceso efectivo de un rol sobre una carpeta específica,
    /// subiendo recursivamente por CarpetaPadreId si no hay regla propia.
    /// </summary>
    public (string NivelEfectivo, bool EsReglaPropia, bool EsHeredado, int? CarpetaOrigenId) ResolverPermisoEnMemoria(
        int carpetaId,
        string? rol,
        IReadOnlyList<Carpeta> todasCarpetas,
        IReadOnlyList<PermisoCarpeta> todosPermisos)
    {
        string rolLimpio = string.IsNullOrWhiteSpace(rol) ? "Lector" : rol.Trim();

        // Regla inmutable: Administrador SIEMPRE tiene Administracion
        if (string.Equals(rolLimpio, "Administrador", StringComparison.OrdinalIgnoreCase))
        {
            return (Administracion, true, false, carpetaId);
        }

        var mapaCarpetas = todasCarpetas.ToDictionary(c => c.Id);
        if (!mapaCarpetas.TryGetValue(carpetaId, out var carpetaActual))
        {
            return (ObtenerNivelPorDefecto(rolLimpio), false, false, null);
        }

        // Construir cadena desde la raíz hasta la carpeta actual para verificar también si un ancestro bloquea con SinAcceso
        var visitados = new HashSet<int>();
        Carpeta? cursor = carpetaActual;
        bool esPrimera = true;

        while (cursor != null && visitados.Add(cursor.Id))
        {
            var regla = todosPermisos.FirstOrDefault(p =>
                p.CarpetaId == cursor.Id &&
                string.Equals(p.Rol, rolLimpio, StringComparison.OrdinalIgnoreCase));

            if (regla != null)
            {
                string nivel = NormalizarNivelAcceso(regla.NivelAcceso) ?? ObtenerNivelPorDefecto(rolLimpio);
                return (nivel, esPrimera, !esPrimera, cursor.Id);
            }

            esPrimera = false;
            if (cursor.CarpetaPadreId.HasValue && mapaCarpetas.TryGetValue(cursor.CarpetaPadreId.Value, out var padre))
            {
                cursor = padre;
            }
            else
            {
                cursor = null;
            }
        }

        return (ObtenerNivelPorDefecto(rolLimpio), false, true, null);
    }

    /// <summary>Resuelve el nivel de acceso efectivo consultando la base de datos.</summary>
    public async Task<string> ObtenerNivelAccesoEfectivoAsync(int? carpetaId, string? departamentoFallback, string? rol)
    {
        if (string.Equals(rol?.Trim(), "Administrador", StringComparison.OrdinalIgnoreCase))
            return Administracion;

        var todasCarpetas = await _context.Carpetas.AsNoTracking().ToListAsync();
        var todosPermisos = await _context.PermisosCarpeta.AsNoTracking().ToListAsync();

        int? idResuelto = ResolverCarpetaId(carpetaId, departamentoFallback, todasCarpetas);
        if (!idResuelto.HasValue)
            return ObtenerNivelPorDefecto(rol);

        return ResolverPermisoEnMemoria(idResuelto.Value, rol, todasCarpetas, todosPermisos).NivelEfectivo;
    }

    /// <summary>Resuelve el CarpetaId de un documento (usando CarpetaId o haciendo fallback por nombre de Departamento).</summary>
    public static int? ResolverCarpetaId(int? carpetaId, string? departamentoFallback, IReadOnlyList<Carpeta> todasCarpetas)
    {
        if (carpetaId.HasValue && todasCarpetas.Any(c => c.Id == carpetaId.Value))
            return carpetaId.Value;

        if (!string.IsNullOrWhiteSpace(departamentoFallback))
        {
            var dep = departamentoFallback.Trim();
            var carpeta = todasCarpetas.FirstOrDefault(c =>
                c.CarpetaPadreId == null && string.Equals(c.Nombre, dep, StringComparison.OrdinalIgnoreCase))
                ?? todasCarpetas.FirstOrDefault(c => string.Equals(c.Nombre, dep, StringComparison.OrdinalIgnoreCase));

            return carpeta?.Id;
        }

        return null;
    }
}
