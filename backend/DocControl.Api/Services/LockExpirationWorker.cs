using DocControl.Api.Data;
using DocControl.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Services;

/// <summary>
/// Servicio centinela en segundo plano que revisa cada 30 segundos si hay bloqueos
/// de edición cuyo tiempo de concesión ha expirado. Por cada bloqueo vencido:
///   1. Captura los datos del usuario que tenía el bloqueo.
///   2. Elimina el registro de InfoBloqueo para liberar el documento.
///   3. Registra la acción EXPIRACION_AUTOMATICA en la bitácora de auditoría.
/// </summary>
public class LockExpirationWorker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LockExpirationWorker> _logger;

    public LockExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<LockExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LockExpirationWorker iniciado. Intervalo de barrido: {Intervalo}s.", Intervalo.TotalSeconds);

        using var timer = new PeriodicTimer(Intervalo);

        // El primer tick ocurre tras el primer intervalo; no se necesita ejecución inmediata.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await BarrerBloqueosExpiradosAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Apagado graceful del host; no es un error.
                break;
            }
            catch (Exception ex)
            {
                // Se registra el error pero el Worker sigue vivo para el siguiente ciclo.
                _logger.LogError(ex, "Error en el ciclo de barrido de bloqueos expirados.");
            }
        }

        _logger.LogInformation("LockExpirationWorker detenido.");
    }

    private async Task BarrerBloqueosExpiradosAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ahora = DateTime.UtcNow;

        // Cargar documentos con bloqueos vencidos (incluir navegación a Bloqueo)
        var documentosVencidos = await db.Documentos
            .Include(d => d.Bloqueo)
            .Where(d => d.Bloqueo != null && d.Bloqueo.FechaExpiracion <= ahora)
            .ToListAsync(ct);

        if (documentosVencidos.Count == 0) return;

        foreach (var doc in documentosVencidos)
        {
            // Capturar datos del bloqueo antes de eliminarlo
            var usuarioBloqueador = doc.Bloqueo!.UsuarioBloqueador;
            var rolBloqueador = doc.Bloqueo.RolBloqueador;

            // Liberar el documento
            db.Bloqueos.Remove(doc.Bloqueo);
            doc.Bloqueo = null;
            doc.Estado = EstadoDocumento.Disponible;

            // Registrar en la bitácora de auditoría
            db.Bitacora.Add(new RegistroBitacora
            {
                FechaHora = ahora,
                Usuario = usuarioBloqueador,
                Rol = rolBloqueador,
                CodigoDocumento = doc.Codigo,
                NombreDocumento = doc.Nombre,
                Version = doc.VersionActual,
                Accion = AccionBitacora.EXPIRACION_AUTOMATICA,
                Observaciones = $"Bloqueo liberado automáticamente por el servidor al agotar el tiempo de concesión. " +
                                $"Usuario afectado: {usuarioBloqueador} ({rolBloqueador}).",
                DatosMaquina = $"Servidor: {Environment.MachineName} | Proceso: LockExpirationWorker"
            });

            _logger.LogInformation(
                "Bloqueo expirado liberado: Doc '{Codigo}' — usuario '{Usuario}'.",
                doc.Codigo, usuarioBloqueador);
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Barrido completado: {Cantidad} bloqueo(s) expirado(s) liberado(s).",
            documentosVencidos.Count);
    }
}

