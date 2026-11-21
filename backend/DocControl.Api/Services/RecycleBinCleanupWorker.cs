using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Services;

/// <summary>
/// Servicio en segundo plano que purga automáticamente de la Papelera de Reciclaje (RF-10)
/// aquellos documentos que hayan superado el plazo de retención configurado en ConfiguracionesSistema.
/// </summary>
public class RecycleBinCleanupWorker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetrasoInicial = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<RecycleBinCleanupWorker> _logger;

    public RecycleBinCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment env,
        ILogger<RecycleBinCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetrasoInicial, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await PurgarDocumentosVencidosAsync(stoppingToken);

        using var timer = new PeriodicTimer(Intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PurgarDocumentosVencidosAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la purga automática de la papelera de reciclaje.");
            }
        }
    }

    private async Task PurgarDocumentosVencidosAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        int diasRetencion = 30;
        var config = await db.ConfiguracionesSistema
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Clave == "PapeleraDiasRetencion", ct);

        if (config != null && int.TryParse(config.Valor, out int diasParseados) && diasParseados >= 1 && diasParseados <= 365)
        {
            diasRetencion = diasParseados;
        }

        var ahora = DateTime.UtcNow;
        var limiteFecha = ahora.AddDays(-diasRetencion);

        var vencidos = await db.Documentos
            .Include(d => d.HistorialVersiones)
            .Where(d => d.Eliminado && d.FechaEliminacion != null && d.FechaEliminacion <= limiteFecha)
            .ToListAsync(ct);

        if (vencidos.Count == 0)
            return;

        string contentRoot = Path.GetFullPath(_env.ContentRootPath);
        string storagePath = Path.GetFullPath(Path.Combine(contentRoot, "Storage"));
        var archivosABorrar = new List<string>();

        foreach (var doc in vencidos)
        {
            var rutasVersiones = doc.HistorialVersiones
                .Where(v => !string.IsNullOrWhiteSpace(v.RutaArchivo))
                .Select(v => v.RutaArchivo!)
                .Distinct()
                .ToList();

            archivosABorrar.AddRange(rutasVersiones);

            db.Bitacora.Add(new RegistroBitacora
            {
                FechaHora = ahora,
                Usuario = "Sistema (Purga Automática)",
                Rol = RolUsuario.Administrador,
                CodigoDocumento = doc.Codigo,
                NombreDocumento = doc.Nombre,
                Version = doc.VersionActual,
                Accion = AccionBitacora.PURGA_AUTOMATICA_PAPELERA,
                Observaciones = $"Documento purgado automáticamente tras cumplir el plazo de retención de {diasRetencion} días en papelera " +
                                $"(eliminado el {doc.FechaEliminacion:dd/MM/yyyy HH:mm:ss} UTC). Versiones eliminadas: {doc.HistorialVersiones.Count}.",
                DatosMaquina = $"Servidor: {Environment.MachineName} | Proceso: RecycleBinCleanupWorker"
            });

            db.Documentos.Remove(doc);
        }

        await db.SaveChangesAsync(ct);

        foreach (var rutaRelativa in archivosABorrar.Distinct())
        {
            BorrarArchivoFisicoSeguro(rutaRelativa, contentRoot, storagePath);
        }

        _logger.LogInformation("Purga automática de papelera completada: {Cantidad} documento(s) eliminado(s).", vencidos.Count);
    }

    private static void BorrarArchivoFisicoSeguro(string rutaArchivo, string contentRoot, string storagePath)
    {
        try
        {
            string? rutaResuelta = null;
            string candidataStorage = Path.GetFullPath(Path.Combine(storagePath, Path.GetFileName(rutaArchivo)));
            if (candidataStorage.StartsWith(storagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(candidataStorage))
            {
                rutaResuelta = candidataStorage;
            }
            else if (File.Exists(rutaArchivo))
            {
                rutaResuelta = Path.GetFullPath(rutaArchivo);
            }
            else
            {
                string candidataRoot = Path.GetFullPath(Path.Combine(contentRoot, rutaArchivo));
                if (candidataRoot.StartsWith(contentRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(candidataRoot))
                {
                    rutaResuelta = candidataRoot;
                }
            }

            if (rutaResuelta != null && File.Exists(rutaResuelta))
            {
                File.Delete(rutaResuelta);
            }
        }
        catch
        {
            // Limpieza best-effort de archivos físicos
        }
    }
}
