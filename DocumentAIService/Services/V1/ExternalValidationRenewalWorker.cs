using DocumentAIService.Configuration;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Services.V1;

/// <summary>
/// Segue o padrão BackgroundService já utilizado no ecossistema. Fica desabilitado por padrão
/// e somente consulta documentos com NextValidationAt elegível e indexado; não varre entidades
/// indiscriminadamente nem chama fornecedor sem adaptador oficialmente implementado.
/// </summary>
public sealed class ExternalValidationRenewalWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DvsOptions> options,
    TimeProvider timeProvider,
    ILogger<ExternalValidationRenewalWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.Renewal.ScanIntervalMinutes));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExternalValidationRenewalWorkerStarted IntervalMinutes={IntervalMinutes} LeadDays={LeadDays}", _interval.TotalMinutes, options.Value.Renewal.LeadDays);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPersistedValidationService>();
                var marked = await service.MarkExternalValidationsDueAsync(stoppingToken);
                if (marked > 0) logger.LogInformation("ExternalValidationRenewalMarkedDue Count={Count}", marked);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExternalValidationRenewalIterationFailed");
            }

            try { await Task.Delay(_interval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        logger.LogInformation("ExternalValidationRenewalWorkerStopped");
    }
}
