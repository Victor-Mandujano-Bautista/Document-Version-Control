using DocControl.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DocControl.Api.Services;

/// <summary>Estado compartido de la programación de respaldos automáticos (RF-21).</summary>
public static class BackupScheduleState
{
    public static DateTime? NextScheduledRunUtc { get; set; }
}

/// <summary>
/// Worker en segundo plano que ejecuta respaldos automáticos diarios (.zip) y expone la próxima fecha programada (RF-21).
/// </summary>
public class ScheduledBackupWorker : BackgroundService
{
    private static readonly TimeSpan RetrasoInicial = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ScheduledBackupWorker> _logger;

    public ScheduledBackupWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ScheduledBackupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool enabled = _config.GetValue<bool?>("BackupSettings:Enabled") ?? true;
        int intervalHours = Math.Max(1, _config.GetValue<int?>("BackupSettings:IntervalHours") ?? 24);
        var intervalo = TimeSpan.FromHours(intervalHours);

        if (!enabled)
        {
            BackupScheduleState.NextScheduledRunUtc = null;
            _logger.LogInformation("ScheduledBackupWorker deshabilitado por configuración.");
            return;
        }

        BackupScheduleState.NextScheduledRunUtc = DateTime.UtcNow.Add(RetrasoInicial);

        try
        {
            await Task.Delay(RetrasoInicial, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await VerificarYCrearRespaldoAutomaticoAsync(intervalo, stoppingToken);

        using var timer = new PeriodicTimer(intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EjecutarRespaldoProgramadoAsync(intervalo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el ciclo de respaldo automático programado.");
                BackupScheduleState.NextScheduledRunUtc = DateTime.UtcNow.Add(intervalo);
            }
        }
    }

    private async Task VerificarYCrearRespaldoAutomaticoAsync(TimeSpan intervalo, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ultimoAuto = await db.RegistrosRespaldo
                .AsNoTracking()
                .Where(r => r.Tipo == "Automatico" && r.Estado == "Completado")
                .OrderByDescending(r => r.FechaCreacion)
                .FirstOrDefaultAsync(ct);

            var ahora = DateTime.UtcNow;
            if (ultimoAuto == null || (ahora - ultimoAuto.FechaCreacion) >= intervalo)
            {
                var backupService = scope.ServiceProvider.GetRequiredService<BackupService>();
                await backupService.CreateBackupAsync(
                    "Automatico",
                    "Sistema (Programado)",
                    $"Servidor: {Environment.MachineName} | Proceso: ScheduledBackupWorker",
                    ct);

                BackupScheduleState.NextScheduledRunUtc = DateTime.UtcNow.Add(intervalo);
            }
            else
            {
                BackupScheduleState.NextScheduledRunUtc = ultimoAuto.FechaCreacion.Add(intervalo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en la verificación inicial de respaldo automático.");
            BackupScheduleState.NextScheduledRunUtc = DateTime.UtcNow.Add(intervalo);
        }
    }

    private async Task EjecutarRespaldoProgramadoAsync(TimeSpan intervalo, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var backupService = scope.ServiceProvider.GetRequiredService<BackupService>();
        await backupService.CreateBackupAsync(
            "Automatico",
            "Sistema (Programado)",
            $"Servidor: {Environment.MachineName} | Proceso: ScheduledBackupWorker",
            ct);

        BackupScheduleState.NextScheduledRunUtc = DateTime.UtcNow.Add(intervalo);
    }
}
