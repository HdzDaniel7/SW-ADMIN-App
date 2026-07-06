using System.Runtime.InteropServices;
using SolidWorks.Interop.swdocumentmgr;
using SWDataExtractor.Core.Contratos;

namespace SWDataExtractor.DocManager;

public class ExtractorDocManager : IExtractorCad
{
    private readonly string _licencia;

    public ExtractorDocManager(string licenciaKey) => _licencia = licenciaKey;

    public string Nombre => "DocManager";
    public AlcanceExtraccion Capacidades => AlcanceExtraccion.Rapida;

    public bool PuedeProcesar(string ruta) =>
        ruta.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase) ||
        ruta.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);

    public Task<ResultadoExtraccion> ExtraerAsync(SolicitudExtraccion solicitud, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_licencia))
            return Task.FromResult(ErrorResult(
                "SwDmLicenseKey no configurada. Ejecutar: " +
                "dotnet user-secrets set \"DocManager:LicenciaKey\" \"CLAVE\""));

        ISwDMApplication4? appDm = null;
        ISwDMDocument19?   doc   = null;
        ISwDMConfigurationMgr? cfgMgr = null;
        try
        {
            appDm = CrearAppDm();

            bool esPieza = solicitud.Ruta.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase);
            var  tipoDoc = esPieza
                ? SwDmDocumentType.swDmDocumentPart
                : SwDmDocumentType.swDmDocumentAssembly;

            // VERIFICAR-API: firma de ISwDMApplication4.GetDocument (orden/tipo de parámetros,
            // significado exacto del bool "true" como ReadOnly/WriteMode) sin validar en help.solidworks.com.
            var docObj = appDm.GetDocument(
                solicitud.Ruta, tipoDoc, true, out SwDmDocumentOpenError errApertura);
            if (errApertura != SwDmDocumentOpenError.swDmDocumentOpenErrorNone || docObj == null)
                return Task.FromResult(ErrorResult($"Error al abrir documento: {errApertura}"));

            doc = (ISwDMDocument19)docObj;

            // ── Metadatos ────────────────────────────────────────────────────
            int     versionSw = doc.GetVersion();
            string? autor     = doc.Author;

            // ── Configuraciones ───────────────────────────────────────────────
            cfgMgr = (ISwDMConfigurationMgr)doc.ConfigurationManager;
            var nombresObj = cfgMgr.GetConfigurationNames() as object[];
            var nombres    = nombresObj?.Cast<string>().ToArray() ?? [];
            string cfgActiva = cfgMgr.GetActiveConfigurationName() ?? nombres.FirstOrDefault() ?? "";

            var dtoConfigs = nombres.Select(n =>
                new DatosConfiguracion(n, n == cfgActiva, false)).ToList();

            // ── Propiedades nivel documento ───────────────────────────────────
            var propiedades = new List<DatosPropiedad>();
            ExtraerPropiedadesDoc(doc, propiedades);

            // ── Propiedades nivel configuración ───────────────────────────────
            foreach (string nombreCfg in nombres)
            {
                ISwDMConfiguration? cfgObj = null;
                try
                {
                    cfgObj = (ISwDMConfiguration)cfgMgr.GetConfigurationByName(nombreCfg);
                    ExtraerPropiedadesCfg(cfgObj, nombreCfg, propiedades);
                }
                finally
                {
                    if (cfgObj is not null) Marshal.ReleaseComObject(cfgObj);
                }
            }

            // ── Preview PNG (desde configuración activa) ──────────────────────
            byte[]? preview = null;
            try
            {
                ISwDMConfiguration10? cfgPrev = null;
                try
                {
                    cfgPrev = cfgMgr.GetConfigurationByName(cfgActiva) as ISwDMConfiguration10;
                    // VERIFICAR-API: GetPreviewPNGBitmap — parámetro de salida (out _) y formato exacto
                    // del byte[] devuelto sin confirmar contra help.solidworks.com.
                    if (cfgPrev is not null)
                        preview = cfgPrev.GetPreviewPNGBitmap(out _) as byte[];
                }
                finally
                {
                    if (cfgPrev is not null) Marshal.ReleaseComObject(cfgPrev);
                }
            }
            catch { /* preview es opcional */ }

            // ── Componentes de ensamble ───────────────────────────────────────
            var componentes = new List<DatosComponente>();
            if (!esPieza)
            {
                ISwDMConfiguration10? cfgAsm = null;
                try
                {
                    cfgAsm = cfgMgr.GetConfigurationByName(cfgActiva) as ISwDMConfiguration10;
                    if (cfgAsm is not null)
                        ExtraerComponentes(cfgAsm, componentes);
                }
                finally
                {
                    if (cfgAsm is not null) Marshal.ReleaseComObject(cfgAsm);
                }
            }

            return Task.FromResult(new ResultadoExtraccion
            {
                Estado          = EstadoExtraccion.Ok,
                Archivo         = new DatosArchivo(
                    esPieza ? TipoArchivoCad.Pieza : TipoArchivoCad.Ensamble,
                    VersionSw: versionSw,
                    Autor:     autor,
                    preview),
                Configuraciones = dtoConfigs,
                Propiedades     = propiedades,
                Componentes     = componentes
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Excepción: {ex.Message}"));
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

    private static void ExtraerPropiedadesDoc(ISwDMDocument19 doc, List<DatosPropiedad> destino)
    {
        var noms = doc.GetCustomPropertyNames() as object[];
        if (noms is null) return;
        foreach (string nombre in noms)
        {
            string valor = doc.GetCustomProperty(nombre, out SwDmCustomInfoType tipo);
            destino.Add(new DatosPropiedad(null, nombre, valor, valor, tipo.ToString()));
        }
    }

    private static void ExtraerPropiedadesCfg(
        ISwDMConfiguration cfg, string nombreCfg, List<DatosPropiedad> destino)
    {
        var noms = cfg.GetCustomPropertyNames() as object[];
        if (noms is null) return;
        foreach (string nombre in noms)
        {
            string valor = cfg.GetCustomProperty(nombre, out SwDmCustomInfoType tipo);
            destino.Add(new DatosPropiedad(nombreCfg, nombre, valor, valor, tipo.ToString()));
        }
    }

    // VERIFICAR-API: ISwDMConfiguration10.GetComponents — se asume que devuelve una entrada
    // por instancia de componente (incluyendo duplicados por patrón/matriz) sin confirmar en
    // help.solidworks.com; el agrupado posterior asume ese comportamiento.
    private static void ExtraerComponentes(ISwDMConfiguration10 cfg, List<DatosComponente> destino)
    {
        var compsObj = cfg.GetComponents() as object[];
        if (compsObj is null) return;

        // DocManager devuelve una entrada por instancia; agrupar para respetar la UNIQUE constraint
        // UNIQUE(ensamble_archivo_id, ensamble_config_id, ruta_referenciada, configuracion_usada, suprimido)
        var agrupados = new Dictionary<(string, string?, bool), DatosComponente>(compsObj.Length);

        foreach (var obj in compsObj)
        {
            var comp = obj as ISwDMComponent10;
            if (comp is null) continue;
            try
            {
                string ruta      = comp.PathName ?? "";
                string? cfgUsada = comp.ConfigurationName;
                bool suprimido   = comp.IsSuppressed();
                bool esEnvelope  = comp.IsEnvelope();

                var key = (ruta, cfgUsada, suprimido);
                if (agrupados.TryGetValue(key, out var existente))
                    agrupados[key] = existente with { Cantidad = existente.Cantidad + 1 };
                else
                    agrupados[key] = new DatosComponente(ruta, cfgUsada, 1, suprimido, false, esEnvelope);
            }
            finally
            {
                Marshal.ReleaseComObject(comp);
            }
        }

        destino.AddRange(agrupados.Values);
    }

    // ISwDMClassFactory (ProgID: SwDocumentMgr.SwDMClassFactory) crea la instancia de la app con la licencia.
    private ISwDMApplication4 CrearAppDm()
    {
        var tipo = Type.GetTypeFromProgID("SwDocumentMgr.SwDMClassFactory")
                   ?? throw new InvalidOperationException(
                       "ProgID 'SwDocumentMgr.SwDMClassFactory' no encontrado. " +
                       "Verifique que SolidWorks esté instalado correctamente.");
        var factory = (ISwDMClassFactory)Activator.CreateInstance(tipo)!;
        var app     = factory.GetApplication(_licencia);
        Marshal.ReleaseComObject(factory);
        return (ISwDMApplication4)app;
    }

    private static ResultadoExtraccion ErrorResult(string msg) =>
        new() { Estado = EstadoExtraccion.Error, MensajeError = msg };
}
