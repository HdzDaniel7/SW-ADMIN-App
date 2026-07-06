namespace SWDataExtractor.Core.Contratos;

public interface IExtractorCad
{
    string Nombre { get; }
    AlcanceExtraccion Capacidades { get; }
    bool PuedeProcesar(string ruta);
    Task<ResultadoExtraccion> ExtraerAsync(SolicitudExtraccion solicitud, CancellationToken ct);
}
