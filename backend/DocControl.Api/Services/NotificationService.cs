using System.Text.RegularExpressions;
using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Services;

/// <summary>
/// Servicio centralizado de notificaciones internas (RF-17) y alertas de liberación de bloqueo.
/// </summary>
public class NotificationService
{
    public record PerfilDemo(string NombreCompleto, string PrimerNombre, string Rol);

    public static readonly PerfilDemo[] Perfiles =
    [
        new("Elena Martínez", "Elena", "Administrador"),
        new("Roberto Díaz", "Roberto", "Revisor"),
        new("Carlos Ruiz", "Carlos", "Editor"),
        new("Ana Gómez", "Ana", "Lector")
    ];

    /// <summary>
    /// Resuelve un perfil válido únicamente por coincidencia exacta con el primer nombre o nombre completo
    /// (insensible a mayúsculas y acentos). Si no coincide con ningún usuario válido, devuelve null.
    /// </summary>
    public static PerfilDemo? ResolverPerfilPorMencion(string? mencion)
    {
        if (string.IsNullOrWhiteSpace(mencion)) return null;
        string limpio = QuitarAcentos(mencion.Trim()).ToLowerInvariant();
        if (limpio.Length == 0) return null;

        foreach (var p in Perfiles)
        {
            string pPrimer = QuitarAcentos(p.PrimerNombre).ToLowerInvariant();
            string pCompleto = QuitarAcentos(p.NombreCompleto).ToLowerInvariant();
            if (limpio == pPrimer || limpio == pCompleto)
                return p;
        }
        return null;
    }

    /// <summary>
    /// Verificación estricta de destinatario:
    /// Coincide únicamente si el RolDestinatario es exactamente igual al rol activo
    /// O si el Destinatario coincide exactamente con el primer nombre o nombre completo del usuario activo.
    /// </summary>
    public static bool CoincideConSesion(string? destinatario, string? rolDestinatario, string? usuarioSesion, string? rolSesion)
    {
        string rolAct = rolSesion?.Trim() ?? string.Empty;
        string usuAct = usuarioSesion?.Trim() ?? string.Empty;

        // Si no se envió ni usuario ni rol en la consulta, no devolver notificaciones de nadie
        if (rolAct.Length == 0 && usuAct.Length == 0)
            return false;

        string rolDest = rolDestinatario?.Trim() ?? string.Empty;
        string usuDest = destinatario?.Trim() ?? string.Empty;

        if (rolAct.Length > 0 && rolDest.Length > 0 &&
            string.Equals(rolDest, rolAct, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (usuAct.Length > 0 && usuDest.Length > 0)
        {
            string uLimpio = QuitarAcentos(usuAct).ToLowerInvariant();
            string uPrimer = uLimpio.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

            string dLimpio = QuitarAcentos(usuDest).ToLowerInvariant();
            string dPrimer = dLimpio.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

            if (uLimpio.Length > 0 && (dLimpio == uLimpio || (uPrimer.Length > 0 && dPrimer == uPrimer)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Trigger 1: Menciones explícitas @Nombre en comentarios.
    /// Crea notificaciones EXCLUSIVAMENTE para los usuarios etiquetados con @Nombre
    /// (excluyendo al propio autor del comentario si se auto-mencionó).
    /// </summary>
    public static void NotificarMencionesEnComentario(
        AppDbContext db,
        Documento doc,
        string autorComentario,
        string rolAutor,
        string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;

        var ahora = DateTime.UtcNow;
        string preview = texto.Length > 110 ? texto[..110] + "..." : texto;
        var destinatariosNotificados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var matches = Regex.Matches(texto, @"@([A-Za-zÁÉÍÓÚáéíóúÑñ]+)");
        foreach (Match m in matches)
        {
            var perfil = ResolverPerfilPorMencion(m.Groups[1].Value);
            if (perfil == null) continue;

            // Excluir al propio autor del comentario si se auto-mencionó
            if (CoincideConSesion(perfil.NombreCompleto, perfil.Rol, autorComentario, rolAutor))
                continue;

            // Evitar duplicados si mencionaron dos veces a la misma persona en el mismo comentario
            if (!destinatariosNotificados.Add(perfil.Rol))
                continue;

            db.Notificaciones.Add(new Notificacion
            {
                Destinatario = perfil.NombreCompleto,
                RolDestinatario = perfil.Rol,
                Tipo = "MENCION",
                Titulo = $"Te mencionaron en {doc.Codigo}",
                Mensaje = $"{autorComentario} te mencionó en '{doc.Nombre}': \"{preview}\"",
                DocumentoId = doc.Id,
                CodigoDocumento = doc.Codigo,
                Leida = false,
                FechaCreacion = ahora
            });
        }
    }

    /// <summary>Trigger 2: Solicitud de revisión enviada por Editor o Administrador.</summary>
    public static void NotificarSolicitudRevision(AppDbContext db, Documento doc, string solicitante, RolUsuario rolSolicitante)
    {
        var ahora = DateTime.UtcNow;

        // Notificar siempre a Revisor (Roberto Díaz)
        db.Notificaciones.Add(new Notificacion
        {
            Destinatario = "Roberto Díaz",
            RolDestinatario = RolUsuario.Revisor.ToString(),
            Tipo = "SOLICITUD_REVISION",
            Titulo = $"Nueva solicitud de revisión: {doc.Codigo}",
            Mensaje = $"{solicitante} solicitó revisar '{doc.Nombre}' ({doc.VersionActual}).",
            DocumentoId = doc.Id,
            CodigoDocumento = doc.Codigo,
            Leida = false,
            FechaCreacion = ahora
        });

        // Notificar también a Administrador si quien solicitó no fue Administrador
        if (rolSolicitante != RolUsuario.Administrador)
        {
            db.Notificaciones.Add(new Notificacion
            {
                Destinatario = "Elena Martínez",
                RolDestinatario = RolUsuario.Administrador.ToString(),
                Tipo = "SOLICITUD_REVISION",
                Titulo = $"Nueva solicitud de revisión: {doc.Codigo}",
                Mensaje = $"{solicitante} solicitó revisar '{doc.Nombre}' ({doc.VersionActual}).",
                DocumentoId = doc.Id,
                CodigoDocumento = doc.Codigo,
                Leida = false,
                FechaCreacion = ahora
            });
        }
    }

    /// <summary>Trigger 3: Dictamen de revisión emitido por Revisor o Administrador.</summary>
    public static void NotificarDictamenRevision(AppDbContext db, Documento doc, string revisor, bool aprobado, string comentario)
    {
        var ahora = DateTime.UtcNow;
        string estadoTitulo = aprobado ? "Aprobada" : "Con observaciones";
        string estadoTexto = aprobado ? "Aprobado" : "Rechazado con observaciones";
        string resumenObs = comentario.Length > 120 ? comentario[..120] + "..." : comentario;

        db.Notificaciones.Add(new Notificacion
        {
            Destinatario = "Carlos Ruiz",
            RolDestinatario = RolUsuario.Editor.ToString(),
            Tipo = "REVISION_DICTAMINADA",
            Titulo = $"Revisión {estadoTitulo}: {doc.Codigo}",
            Mensaje = $"{revisor} marcó '{doc.Nombre}' como {estadoTexto}. {resumenObs}",
            DocumentoId = doc.Id,
            CodigoDocumento = doc.Codigo,
            Leida = false,
            FechaCreacion = ahora
        });
    }

    /// <summary>
    /// Trigger 4: Archivo liberado (Check-in, Force-Unlock o Expiración automática TTL).
    /// Notifica a todos los usuarios suscritos en la lista de espera del documento.
    /// </summary>
    public static async Task NotifyLockReleasedAsync(AppDbContext db, Documento doc, string motivoLiberacion, CancellationToken ct = default)
    {
        var suscripciones = await db.SuscripcionesLiberacion
            .Where(s => s.DocumentoId == doc.Id && !s.Notificado)
            .ToListAsync(ct);

        if (suscripciones.Count == 0) return;

        var ahora = DateTime.UtcNow;
        foreach (var sub in suscripciones)
        {
            sub.Notificado = true;
            db.Notificaciones.Add(new Notificacion
            {
                Destinatario = sub.Usuario,
                RolDestinatario = sub.Rol,
                Tipo = "ARCHIVO_LIBERADO",
                Titulo = $"Archivo disponible para edición: {doc.Codigo}",
                Mensaje = $"'{doc.Nombre}' ya fue liberado ({motivoLiberacion}). Ya puedes bloquearlo para editar.",
                DocumentoId = doc.Id,
                CodigoDocumento = doc.Codigo,
                Leida = false,
                FechaCreacion = ahora
            });
        }
    }

    private static string QuitarAcentos(string texto)
    {
        var norm = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
