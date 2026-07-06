namespace SWDataExtractor.Core.Contratos;

public interface IEscritorPropiedades
{
    Task<ResultadoEscrituraLote> EscribirAsync(LoteEscritura lote, CancellationToken ct);
}
