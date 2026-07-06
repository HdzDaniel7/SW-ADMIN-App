using SWDataExtractor.Application.Servicios;

namespace SWDataExtractor.Batch;

public class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("SWDataExtractor Batch iniciado");

        await using var scope = scopeFactory.CreateAsyncScope();
        var escaneador  = scope.ServiceProvider.GetRequiredService<EscaneadorCarpetas>();
        var orquestador = scope.ServiceProvider.GetRequiredService<OrquestadorExtraccion>();

        var resumen = await escaneador.EscanearAsync(ct);
        logger.LogInformation(
            "Escaneo: Nuevos={N} Actualizados={A} SinCambios={S} Eliminados={E}",
            resumen.Nuevos, resumen.Actualizados, resumen.SinCambios, resumen.Eliminados);

        await orquestador.ProcesarPendientesAsync(ModoExtraccion.Auto, ct);

        logger.LogInformation("Ciclo completado");
    }
}
