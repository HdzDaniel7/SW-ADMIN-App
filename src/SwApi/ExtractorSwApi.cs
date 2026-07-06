using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidWorks.Interop.sldworks;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Core.Contratos;

namespace SWDataExtractor.SwApi;

// Extracción profunda via COM API de SolidWorks.
// Requiere que SolidWorks esté en ejecución con al menos una ventana abierta.
// Extrae: propiedades custom, configuraciones, componentes (BOM), features, roscas y propiedades físicas.
public class ExtractorSwApi : IExtractorCad
{
    // ── Constantes SW (evita dependencia en swconst.dll) ─────────────────────
    private const int SW_DOC_PART        = 1;
    private const int SW_DOC_ASSEMBLY    = 2;
    private const int SW_OPT_SILENT      = 1;   // swOpenDocOptions_Silent
    private const int SW_WZD_COUNTERBORE = 0;
    private const int SW_WZD_COUNTERSINK = 1;
    private const int SW_WZD_HOLE        = 2;
    private const int SW_WZD_PIPE_TAP    = 3;
    private const int SW_WZD_TAP         = 4;
    private const int SW_END_THRU_ALL    = 1;   // swEndCondThroughAll
    private const int SW_SUM_AUTHOR      = 2;   // swSumInfoAuthor

    private readonly int _reiniciarCadaN;
    private readonly ILogger<ExtractorSwApi> _logger;
    private int _archivosDesdeUltimoReinicio;

    public ExtractorSwApi(IOptions<ConfiguracionExtraccion> opciones, ILogger<ExtractorSwApi> logger)
    {
        _reiniciarCadaN = opciones.Value.ReiniciarSwCadaNArchivos;
        _logger         = logger;
    }

    public string Nombre => "SwApi";
    public AlcanceExtraccion Capacidades => AlcanceExtraccion.Profunda;

    public bool PuedeProcesar(string ruta)
    {
        var ext = Path.GetExtension(ruta).ToLowerInvariant();
        return ext is ".sldprt" or ".sldasm" or ".stp" or ".step";
    }

    public Task<ResultadoExtraccion> ExtraerAsync(SolicitudExtraccion solicitud, CancellationToken ct)
    {
        ISldWorks?   swApp       = null;
        IModelDoc2?  doc         = null;
        bool         docYaEstaba = false;
        try
        {
            swApp = ObtenerSwApp();
            if (swApp is null)
                return Task.FromResult(ErrorResult(
                    "SolidWorks no está en ejecución. " +
                    "Ábrelo antes de usar el modo de extracción profunda."));

            swApp.UserControlBackground = true;

            var  ext    = Path.GetExtension(solicitud.Ruta).ToLowerInvariant();
            bool esStep = ext is ".stp" or ".step";

            // Si el archivo ya está abierto en SW, usarlo directamente (evita error 2097152).
            doc = BuscarDocumentoAbierto(swApp, solicitud.Ruta);
            if (doc is not null)
            {
                docYaEstaba = true;
                _logger.LogDebug("Documento ya abierto en SW, reutilizando: {Ruta}", solicitud.Ruta);
            }
            else if (esStep)
            {
                // Suprimir diálogos de importación STEP antes de abrir
                // VERIFICAR-API: swImportStepShowOptions = 415
                try { swApp.SetUserPreferenceIntegerValue(415, 0); } catch { /* no crítico */ }

                // Intentar como pieza primero, luego ensamble
                int err1 = 0, adv1 = 0;
                doc = swApp.OpenDoc6(solicitud.Ruta, SW_DOC_PART, SW_OPT_SILENT, "", ref err1, ref adv1);
                if (doc is null)
                {
                    int err2 = 0, adv2 = 0;
                    doc = swApp.OpenDoc6(solicitud.Ruta, SW_DOC_ASSEMBLY, SW_OPT_SILENT, "", ref err2, ref adv2);
                    if (doc is null)
                        return Task.FromResult(ErrorResult(
                            $"STEP no se pudo importar (código pieza={err1}, ensamble={err2}). " +
                            "Abre el archivo manualmente en SW al menos una vez para crear la caché de importación."));
                }
            }
            else
            {
                bool esPiezaExt = ext == ".sldprt";
                int erroresSw = 0, adv = 0;
                doc = swApp.OpenDoc6(solicitud.Ruta, esPiezaExt ? SW_DOC_PART : SW_DOC_ASSEMBLY,
                    SW_OPT_SILENT, "", ref erroresSw, ref adv);
                if (doc is null)
                    return Task.FromResult(ErrorResult($"No se pudo abrir el archivo: código {erroresSw}"));
            }

            // Tipo pieza vs ensamble: detectado via interfaz COM (funciona para todo tipo de doc)
            bool esPieza = doc is not IAssemblyDoc;

            ct.ThrowIfCancellationRequested();

            // Configuración activa
            string cfgActiva = "";
            var cfgObj = doc.GetActiveConfiguration() as IConfiguration;
            if (cfgObj is not null) { cfgActiva = cfgObj.Name ?? ""; Marshal.ReleaseComObject(cfgObj); }

            var advertencias = new List<string>();

            // ── Metadatos del archivo ─────────────────────────────────────────
            int?    versionSw = ParsearVersion(swApp.RevisionNumber());
            string? autor     = ExtraerAutor(doc, advertencias);
            var     tipoDto   = esPieza ? TipoArchivoCad.Pieza : TipoArchivoCad.Ensamble;

            // ── Configuraciones ───────────────────────────────────────────────
            var configuraciones = ExtraerConfiguraciones(doc, cfgActiva, advertencias);

            ct.ThrowIfCancellationRequested();

            // ── Propiedades custom (nivel documento + nivel configuración) ────
            var cfgNombres = configuraciones.Select(c => c.Nombre).ToList();
            var propiedades = ExtraerPropiedades(doc, cfgNombres, advertencias);

            ct.ThrowIfCancellationRequested();

            // ── Features y roscas ────────────────────────────────────────────
            // Se omiten si el alcance solicitado no las pide (modo Rápido vía SwApi: recorrer
            // el árbol de features es el paso más caro y es lo único que distingue Rápida de
            // Profunda; así SwApi puede servir de sustituto liviano de DocManager sin licencia).
            // Los archivos STEP importados tampoco tienen árbol de features nativo.
            var features = new List<DatosFeature>();
            var roscas   = new List<DatosRosca>();
            bool pideFeatures = solicitud.Alcance.HasFlag(AlcanceExtraccion.Features) ||
                                solicitud.Alcance.HasFlag(AlcanceExtraccion.Roscas);
            if (!esStep && pideFeatures)
                IterarFeatures(doc, features, roscas, advertencias);

            ct.ThrowIfCancellationRequested();

            // ── Propiedades físicas (piezas) ──────────────────────────────────
            List<DatosPropiedadesFisicas> fisicas = [];
            if (esPieza && solicitud.Alcance.HasFlag(AlcanceExtraccion.Fisicas))
            {
                var fis = ExtraerPropsFisicas(doc, cfgActiva, advertencias);
                if (fis is not null) fisicas = [fis];
            }

            // ── Componentes / BOM (ensambles) ─────────────────────────────────
            var componentes = new List<DatosComponente>();
            if (!esPieza && solicitud.Alcance.HasFlag(AlcanceExtraccion.Estructura))
                componentes = ExtraerComponentes(doc, advertencias);

            _archivosDesdeUltimoReinicio++;
            if (_archivosDesdeUltimoReinicio >= _reiniciarCadaN)
            {
                _logger.LogWarning(
                    "Se alcanzó el límite de {N} archivos sin reiniciar SolidWorks. " +
                    "Considere cerrar y reabrir SW para liberar memoria COM.",
                    _reiniciarCadaN);
                _archivosDesdeUltimoReinicio = 0;
            }

            return Task.FromResult(new ResultadoExtraccion
            {
                Estado          = EstadoExtraccion.Ok,
                Archivo         = new DatosArchivo(tipoDto, versionSw, autor, null),
                Configuraciones = configuraciones,
                Propiedades     = propiedades,
                Features        = features,
                Roscas          = roscas,
                Fisicas         = fisicas,
                Componentes     = componentes,
                Advertencias    = advertencias
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ErrorResult("Extracción cancelada."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Excepción: {ex.Message}"));
        }
        finally
        {
            if (doc is not null)
            {
                // Solo cerrar si nosotros abrimos el documento; si ya estaba abierto, solo liberar la referencia COM.
                if (!docYaEstaba)
                    try { swApp?.CloseDoc(solicitud.Ruta); } catch { /* no crítico */ }
                Marshal.ReleaseComObject(doc);
            }
            // swApp se obtiene vía ROT (GetActiveObject) en cada llamada: es una RCW nueva
            // que debe liberarse aquí; no cierra la instancia real de SolidWorks, solo la referencia COM local.
            if (swApp is not null) Marshal.ReleaseComObject(swApp);
        }
    }

    // Busca un documento ya abierto en SolidWorks por ruta (evita llamar OpenDoc6 sobre archivos en uso).
    // Devuelve una referencia COM que el llamador debe liberar con ReleaseComObject cuando termine.
    private IModelDoc2? BuscarDocumentoAbierto(ISldWorks swApp, string ruta)
    {
        // Intento 1: búsqueda directa por nombre de archivo (más eficiente)
        try
        {
            var doc = swApp.GetOpenDocumentByName(ruta) as IModelDoc2;
            if (doc is not null)
            {
                _logger.LogDebug("GetOpenDocumentByName encontró: {Ruta}", ruta);
                return doc;
            }
        }
        catch (Exception ex) { _logger.LogDebug("GetOpenDocumentByName excepción: {Ex}", ex.Message); }

        // Intento 2: recorrer lista y comparar GetPathName (útil si el path difiere en formato)
        try
        {
            string nombreSinExt = Path.GetFileNameWithoutExtension(ruta);
            var actual = swApp.GetFirstDocument() as IModelDoc2;
            while (actual is not null)
            {
                string pathDoc = actual.GetPathName() ?? "";
                _logger.LogDebug("BuscarDoc: '{PathDoc}' vs '{Ruta}'", pathDoc, ruta);

                bool coincide = string.Equals(pathDoc, ruta, StringComparison.OrdinalIgnoreCase)
                    // Para STEP importados SW puede guardar como .sldprt en la misma carpeta
                    || (string.Equals(Path.GetDirectoryName(pathDoc), Path.GetDirectoryName(ruta), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.GetFileNameWithoutExtension(pathDoc), nombreSinExt, StringComparison.OrdinalIgnoreCase));

                if (coincide)
                {
                    _logger.LogDebug("BuscarDoc: coincidencia encontrada '{PathDoc}'", pathDoc);
                    return actual;
                }

                var siguiente = actual.GetNext() as IModelDoc2;
                Marshal.ReleaseComObject(actual);
                actual = siguiente;
            }
        }
        catch (Exception ex) { _logger.LogDebug("BuscarDoc recorrido excepción: {Ex}", ex.Message); }

        _logger.LogDebug("BuscarDoc: ningún doc abierto coincide con '{Ruta}'", ruta);
        return null;
    }

    // ── Metadatos ─────────────────────────────────────────────────────────────

    private static int? ParsearVersion(string? revision)
    {
        // RevisionNumber() devuelve "33.0" para SW 2025 (major = año - 1992)
        if (string.IsNullOrEmpty(revision)) return null;
        var parte = revision.Split('.')[0].Trim();
        return int.TryParse(parte, out int v) ? v : null;
    }

    private static string? ExtraerAutor(IModelDoc2 doc, List<string> advertencias)
    {
        try
        {
            // SummaryInfo es una propiedad indexada — swSumInfoAuthor = 2
            var valor = doc.SummaryInfo[SW_SUM_AUTHOR];
            return valor as string;
        }
        catch (Exception ex)
        {
            advertencias.Add($"Autor: {ex.Message}");
            return null;
        }
    }

    // ── Configuraciones ───────────────────────────────────────────────────────

    private static List<DatosConfiguracion> ExtraerConfiguraciones(
        IModelDoc2 doc, string cfgActiva, List<string> advertencias)
    {
        var resultado = new List<DatosConfiguracion>();
        try
        {
            // VERIFICAR-API: GetConfigurationNames() devuelve object[]
            var nombres = doc.GetConfigurationNames() as object[];
            if (nombres is null) return resultado;

            foreach (string nombre in nombres.Cast<string>())
            {
                bool esActiva   = string.Equals(nombre, cfgActiva, StringComparison.OrdinalIgnoreCase);
                bool esDerivada = false;

                IConfiguration? cfg = null;
                try
                {
                    // VERIFICAR-API: GetConfigurationByName
                    cfg = doc.GetConfigurationByName(nombre) as IConfiguration;
                    if (cfg is not null)
                    {
                        // VERIFICAR-API: IConfiguration.GetParent — null si es raíz
                        var padre = cfg.GetParent() as IConfiguration;
                        esDerivada = padre is not null;
                        if (padre is not null) Marshal.ReleaseComObject(padre);
                    }
                }
                catch { /* no crítico */ }
                finally
                {
                    if (cfg is not null) Marshal.ReleaseComObject(cfg);
                }

                resultado.Add(new DatosConfiguracion(nombre, esActiva, esDerivada));
            }
        }
        catch (Exception ex)
        {
            advertencias.Add($"Configuraciones: {ex.Message}");
        }
        return resultado;
    }

    // ── Propiedades custom ────────────────────────────────────────────────────

    private static List<DatosPropiedad> ExtraerPropiedades(
        IModelDoc2 doc, List<string> cfgNombres, List<string> advertencias)
    {
        var resultado = new List<DatosPropiedad>();
        IModelDocExtension? ext = null;
        try
        {
            ext = (IModelDocExtension)doc.Extension;

            // Nivel documento (clave vacía "")
            LeerPropiedadesDeManager(ext, "", null, resultado, advertencias);

            // Nivel configuración
            foreach (var cfg in cfgNombres)
                LeerPropiedadesDeManager(ext, cfg, cfg, resultado, advertencias);
        }
        catch (Exception ex)
        {
            advertencias.Add($"Propiedades: {ex.Message}");
        }
        finally
        {
            if (ext is not null) Marshal.ReleaseComObject(ext);
        }
        return resultado;
    }

    private static void LeerPropiedadesDeManager(
        IModelDocExtension ext,
        string claveMgr,
        string? configuracion,
        List<DatosPropiedad> resultado,
        List<string> advertencias)
    {
        ICustomPropertyManager? mgr = null;
        try
        {
            // VERIFICAR-API: CustomPropertyManager[string] como indexer o get_CustomPropertyManager
            mgr = (ICustomPropertyManager)ext.CustomPropertyManager[claveMgr];
            if (mgr is null) return;

            // VERIFICAR-API: GetNames() devuelve object[]
            var nombres = mgr.GetNames() as object[];
            if (nombres is null || nombres.Length == 0) return;

            foreach (string nombre in nombres.Cast<string>())
            {
                try
                {
                    // VERIFICAR-API: Get5(name, useCached, out val, out resolved, out wasResolved)
                    string val      = "";
                    string resolved = "";
                    bool   wasRes   = false;
                    mgr.Get5(nombre, false, out val, out resolved, out wasRes);
                    resultado.Add(new DatosPropiedad(configuracion, nombre, val, resolved, "text"));
                }
                catch (Exception ex)
                {
                    advertencias.Add($"Propiedad '{nombre}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            advertencias.Add($"CustomPropertyManager['{claveMgr}']: {ex.Message}");
        }
        finally
        {
            if (mgr is not null) Marshal.ReleaseComObject(mgr);
        }
    }

    // ── Componentes (BOM de ensamble) ─────────────────────────────────────────

    private static List<DatosComponente> ExtraerComponentes(
        IModelDoc2 doc, List<string> advertencias)
    {
        var resultado = new List<DatosComponente>();
        try
        {
            // VERIFICAR-API: IAssemblyDoc.GetComponents(topLevelOnly)
            var asm   = (IAssemblyDoc)doc;
            var comps = asm.GetComponents(true) as object[];
            if (comps is null) return resultado;

            // Agrupar por ruta para obtener cantidad
            var grupos = new Dictionary<string, (string cfgUsada, bool suprimido, bool esToolbox, bool esEnvelope, int cantidad)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var compObj in comps)
            {
                IComponent2? comp = null;
                try
                {
                    comp = compObj as IComponent2;
                    if (comp is null) continue;

                    // VERIFICAR-API: GetPathName
                    string ruta = comp.GetPathName() ?? "";
                    if (string.IsNullOrEmpty(ruta)) continue;

                    // VERIFICAR-API: ReferencedConfiguration o GetReferenceConfiguration
                    string cfgUsada = "";
                    try { cfgUsada = comp.ReferencedConfiguration ?? ""; }
                    catch { }

                    bool suprimido = false;
                    try { suprimido = comp.IsSuppressed(); }
                    catch { }

                    // No existe método toolbox directo; ExcludeFromBOM es el proxy más cercano
                    bool esToolbox = false;
                    try { esToolbox = comp.ExcludeFromBOM; }
                    catch { }

                    bool esEnvelope = false;
                    try { esEnvelope = comp.IsEnvelope(); }
                    catch { }

                    if (grupos.TryGetValue(ruta, out var g))
                        grupos[ruta] = (g.cfgUsada, g.suprimido, g.esToolbox, g.esEnvelope, g.cantidad + 1);
                    else
                        grupos[ruta] = (cfgUsada, suprimido, esToolbox, esEnvelope, 1);
                }
                finally
                {
                    if (comp is not null) Marshal.ReleaseComObject(comp);
                }
            }

            foreach (var (ruta, (cfgUsada, suprimido, esToolbox, esEnvelope, cantidad)) in grupos)
                resultado.Add(new DatosComponente(ruta, cfgUsada, cantidad, suprimido, esToolbox, esEnvelope));
        }
        catch (Exception ex)
        {
            advertencias.Add($"Componentes: {ex.Message}");
        }
        return resultado;
    }

    // ── Iteración de features ─────────────────────────────────────────────────

    private static void IterarFeatures(
        IModelDoc2 doc,
        List<DatosFeature> features,
        List<DatosRosca>   roscas,
        List<string>       advertencias)
    {
        var feat  = doc.FirstFeature() as IFeature;
        int orden = 0;
        while (feat is not null)
        {
            string tipoSw    = feat.GetTypeName2() ?? "";
            string nombre    = feat.Name ?? $"Feature_{orden}";
            bool suprimido   = feat.IsSuppressed();
            string categoria = ClasificarFeature(tipoSw);
            string? paramsJson = null;

            if (!suprimido)
            {
                if (tipoSw == "HoleWzd")
                {
                    var rosca = ExtraerRosca(feat, nombre, advertencias);
                    if (rosca is not null) roscas.Add(rosca);
                }
                else if (tipoSw == "Chamfer")
                {
                    paramsJson = ExtraerParamsChamfer(feat, nombre, advertencias);
                }
            }

            features.Add(new DatosFeature(nombre, tipoSw, categoria, paramsJson, suprimido, orden));

            IFeature? siguiente = null;
            try { siguiente = feat.GetNextFeature() as IFeature; }
            finally { Marshal.ReleaseComObject(feat); }

            feat = siguiente;
            orden++;
        }
    }

    // ── Rosca Hole Wizard ─────────────────────────────────────────────────────

    private static DatosRosca? ExtraerRosca(
        IFeature feat, string nombre, List<string> advertencias)
    {
        object? defObj = null;
        try
        {
            defObj = feat.GetDefinition();
            if (defObj is not IWizardHoleFeatureData2 holeData) return null;

            holeData.AccessSelections(null, null);
            try
            {
                int    tipo          = holeData.Type;
                string estandar      = holeData.Standard ?? "";
                string tamano        = holeData.FastenerSize ?? "";
                double profRoscaMm   = holeData.ThreadDepth * 1000;
                double profBarrenoMm = holeData.Depth * 1000;
                double diaNomMm      = holeData.HoleDiameter > 0
                                       ? holeData.HoleDiameter * 1000
                                       : holeData.Diameter * 1000;
                bool pasante         = holeData.EndCondition == SW_END_THRU_ALL;
                int  cantidad        = Math.Max(1, holeData.GetSketchPointCount());

                string tipoBarreno = tipo switch
                {
                    SW_WZD_TAP         => "rosca",
                    SW_WZD_PIPE_TAP    => "rosca_tubo",
                    SW_WZD_HOLE        => "barreno",
                    SW_WZD_COUNTERBORE => "escariado",
                    SW_WZD_COUNTERSINK => "avellanado",
                    _                  => "otro"
                };

                double? hilosPorPulgada = null;
                int guionIdx = tamano.IndexOf('-');
                if (guionIdx >= 0)
                {
                    var resto = tamano[(guionIdx + 1)..].Trim().Split(' ')[0];
                    if (double.TryParse(resto, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double tpi))
                        hilosPorPulgada = tpi;
                }

                return new DatosRosca(
                    FeatureNombre:        nombre,
                    Designacion:          $"{estandar} {tamano}".Trim(),
                    Estandar:             string.IsNullOrWhiteSpace(estandar) ? null : estandar,
                    TipoBarreno:          tipoBarreno,
                    DiametroNominalMm:    diaNomMm > 0 ? diaNomMm : null,
                    PasoMm:               null,
                    HilosPorPulgada:      hilosPorPulgada,
                    ProfundidadRoscaMm:   profRoscaMm  > 0 ? profRoscaMm  : null,
                    ProfundidadBarrenoMm: profBarrenoMm > 0 ? profBarrenoMm : null,
                    Pasante:              pasante,
                    Cantidad:             cantidad);
            }
            finally
            {
                try { holeData.ReleaseSelectionAccess(); } catch { /* no crítico */ }
            }
        }
        catch (Exception ex)
        {
            advertencias.Add($"HoleWzd '{nombre}': {ex.Message}");
            return null;
        }
        finally
        {
            if (defObj is not null) Marshal.ReleaseComObject(defObj);
        }
    }

    // ── Chaflán ───────────────────────────────────────────────────────────────

    private static string? ExtraerParamsChamfer(
        IFeature feat, string nombre, List<string> advertencias)
    {
        object? defObj = null;
        try
        {
            defObj = feat.GetDefinition();
            if (defObj is not IChamferFeatureData2 chamData) return null;

            chamData.AccessSelections(null, null);
            try
            {
                bool iguales = chamData.EqualDistance;
                double dist1 = chamData.GetEdgeChamferDistance(0) * 1000;
                double dist2 = iguales ? dist1 : chamData.GetEdgeChamferDistance(1) * 1000;
                double angG  = chamData.EdgeChamferAngle * (180.0 / Math.PI);

                return JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["tipoInt"]      = chamData.Type,
                    ["distancia1Mm"] = Math.Round(dist1, 4),
                    ["distancia2Mm"] = Math.Round(dist2, 4),
                    ["anguloGrad"]   = Math.Round(angG, 3),
                    ["cantBordes"]   = chamData.GetEdgeCount(),
                    ["iguales"]      = iguales
                });
            }
            finally
            {
                try { chamData.ReleaseSelectionAccess(); } catch { /* no crítico */ }
            }
        }
        catch (Exception ex)
        {
            advertencias.Add($"Chamfer '{nombre}': {ex.Message}");
            return null;
        }
        finally
        {
            if (defObj is not null) Marshal.ReleaseComObject(defObj);
        }
    }

    // ── Propiedades físicas ───────────────────────────────────────────────────

    private static DatosPropiedadesFisicas? ExtraerPropsFisicas(
        IModelDoc2 doc, string cfgActiva, List<string> advertencias)
    {
        IModelDocExtension? ext = null;
        IMassProperty?      mp  = null;
        try
        {
            string? material = null;
            if (doc is IPartDoc partDoc)
            {
                try { material = partDoc.GetMaterialPropertyName2(cfgActiva, out _); }
                catch { /* no crítico */ }
                if (string.IsNullOrWhiteSpace(material)) material = null;
            }

            ext = (IModelDocExtension)doc.Extension;
            int mpStatus = 0;
            mp  = (IMassProperty)ext.GetMassProperties(0, ref mpStatus);
            if (mp is null || mpStatus != 0) return null;

            mp.UseSystemUnits = true;

            return new DatosPropiedadesFisicas(
                cfgActiva,
                material,
                mp.Density     > 0 ? mp.Density     : null,
                mp.Mass        > 0 ? mp.Mass        : null,
                mp.Volume      > 0 ? mp.Volume      : null,
                mp.SurfaceArea > 0 ? mp.SurfaceArea : null);
        }
        catch (Exception ex)
        {
            advertencias.Add($"Propiedades físicas: {ex.Message}");
            return null;
        }
        finally
        {
            if (mp  is not null) Marshal.ReleaseComObject(mp);
            if (ext is not null) Marshal.ReleaseComObject(ext);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ClasificarFeature(string tipoSw) => tipoSw switch
    {
        "Chamfer"                     => "chaflan",
        "Fillet"                      => "redondeo",
        "HoleWzd"                     => "barreno_asistente",
        "Thread"                      => "rosca",
        "Boss-Extrude" or "Extrusion" => "extrusion",
        "Cut-Extrude"                 => "corte",
        "Revolve" or "Revolution"     => "revolucion",
        "Sweep"                       => "barrido",
        "LPattern" or "CirPattern"    => "patron",
        "RefPlane" or "RefAxis"       => "referencia",
        _                             => "otro"
    };

    // Marshal.GetActiveObject no existe en .NET Core; se usa oleaut32 para acceder al ROT.
    private static ISldWorks? ObtenerSwApp()
    {
        try
        {
            var tipo = Type.GetTypeFromProgID("SldWorks.Application");
            if (tipo is null) return null;
            var clsid = tipo.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out object ppunk);
            return ppunk as ISldWorks;
        }
        catch { return null; }
    }

    [System.Runtime.InteropServices.DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid,
        IntPtr   pvReserved,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.IUnknown)]
        out object ppunk);

    private static ResultadoExtraccion ErrorResult(string msg) =>
        new() { Estado = EstadoExtraccion.Error, MensajeError = msg };
}
