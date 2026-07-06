using System.Runtime.InteropServices;
using SolidWorks.Interop.swdocumentmgr;
using SWDataExtractor.Core.Contratos;

namespace SWDataExtractor.DocManager;

public class EscritorDocManager : IEscritorPropiedades
{
    private readonly string _licencia;

    public EscritorDocManager(string licenciaKey) => _licencia = licenciaKey;

    public Task<ResultadoEscrituraLote> EscribirAsync(LoteEscritura lote, CancellationToken ct)
    {
        var loteId     = Guid.NewGuid().ToString();
        var resultados = new List<ResultadoCambio>();

        if (string.IsNullOrEmpty(_licencia))
        {
            foreach (var c in lote.Cambios)
                resultados.Add(new ResultadoCambio(c, null, "error_otro",
                    "SwDmLicenseKey no configurada."));
            return Task.FromResult(new ResultadoEscrituraLote(loteId, resultados));
        }

        foreach (var cambio in lote.Cambios)
        {
            ct.ThrowIfCancellationRequested();
            ISwDMApplication4? appDm  = null;
            ISwDMDocument19?   doc    = null;
            ISwDMConfigurationMgr? cfgMgr = null;
            try
            {
                appDm = CrearAppDm();

                bool esPieza = cambio.Ruta.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase);
                var  tipoDoc = esPieza
                    ? SwDmDocumentType.swDmDocumentPart
                    : SwDmDocumentType.swDmDocumentAssembly;

                // readOnly=false para permitir escritura
                var docObj = appDm.GetDocument(
                    cambio.Ruta, tipoDoc, false, out SwDmDocumentOpenError errApertura);
                if (errApertura != SwDmDocumentOpenError.swDmDocumentOpenErrorNone || docObj == null)
                {
                    resultados.Add(new ResultadoCambio(cambio, null, "error_otro",
                        $"No se pudo abrir: {errApertura}"));
                    continue;
                }
                doc = (ISwDMDocument19)docObj;

                string? valorAnterior = null;
                string  resultado;
                string? mensajeError  = null;

                if (cambio.Configuracion is null)
                {
                    // ── Nivel documento ──────────────────────────────────────
                    valorAnterior = doc.GetCustomProperty(cambio.Propiedad, out _);
                    bool existe   = PropiedadExisteEnDoc(doc, cambio.Propiedad);
                    if (existe)
                        doc.SetCustomProperty(cambio.Propiedad, cambio.ValorNuevo);
                    else
                        doc.AddCustomProperty(cambio.Propiedad,
                            SwDmCustomInfoType.swDmCustomInfoText, cambio.ValorNuevo);
                    resultado = "ok";
                }
                else
                {
                    // ── Nivel configuración ──────────────────────────────────
                    cfgMgr = (ISwDMConfigurationMgr)doc.ConfigurationManager;
                    ISwDMConfiguration? cfgObj = null;
                    try
                    {
                        cfgObj = (ISwDMConfiguration)cfgMgr.GetConfigurationByName(cambio.Configuracion);
                        if (cfgObj is null)
                        {
                            resultados.Add(new ResultadoCambio(cambio, null, "error_otro",
                                $"Configuración '{cambio.Configuracion}' no encontrada."));
                            continue;
                        }
                        valorAnterior = cfgObj.GetCustomProperty(cambio.Propiedad, out _);
                        bool existe   = PropiedadExisteEnCfg(cfgObj, cambio.Propiedad);
                        if (existe)
                            cfgObj.SetCustomProperty(cambio.Propiedad, cambio.ValorNuevo);
                        else
                            cfgObj.AddCustomProperty(cambio.Propiedad,
                                SwDmCustomInfoType.swDmCustomInfoText, cambio.ValorNuevo);
                        resultado = "ok";
                    }
                    finally
                    {
                        if (cfgObj is not null) Marshal.ReleaseComObject(cfgObj);
                    }
                }

                if (resultado == "ok")
                {
                    var errGuardado = doc.Save();
                    if (errGuardado != SwDmDocumentSaveError.swDmDocumentSaveErrorNone)
                    {
                        resultado    = "error_otro";
                        mensajeError = $"Save falló: {errGuardado}";
                    }
                }

                resultados.Add(new ResultadoCambio(cambio, valorAnterior, resultado, mensajeError));
            }
            catch (Exception ex)
            {
                resultados.Add(new ResultadoCambio(cambio, null, "error_otro", ex.Message));
            }
            finally
            {
                if (cfgMgr is not null) Marshal.ReleaseComObject(cfgMgr);
                if (doc is not null)
                {
                    try { doc.CloseDoc(); } catch { /* no crítico */ }
                    Marshal.ReleaseComObject(doc);
                }
                if (appDm is not null) Marshal.ReleaseComObject(appDm);
            }
        }

        return Task.FromResult(new ResultadoEscrituraLote(loteId, resultados));
    }

    private static bool PropiedadExisteEnDoc(ISwDMDocument19 doc, string propiedad)
    {
        var noms = doc.GetCustomPropertyNames() as object[];
        return noms?.Cast<string>().Contains(propiedad, StringComparer.OrdinalIgnoreCase) == true;
    }

    private static bool PropiedadExisteEnCfg(ISwDMConfiguration cfg, string propiedad)
    {
        var noms = cfg.GetCustomPropertyNames() as object[];
        return noms?.Cast<string>().Contains(propiedad, StringComparer.OrdinalIgnoreCase) == true;
    }

    private ISwDMApplication4 CrearAppDm()
    {
        var tipo = Type.GetTypeFromProgID("SwDocumentMgr.SwDMClassFactory")
                   ?? throw new InvalidOperationException(
                       "ProgID 'SwDocumentMgr.SwDMClassFactory' no encontrado.");
        var factory = (ISwDMClassFactory)Activator.CreateInstance(tipo)!;
        var app     = factory.GetApplication(_licencia);
        Marshal.ReleaseComObject(factory);
        return (ISwDMApplication4)app;
    }
}
