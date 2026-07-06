using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.Application.Servicios;

public enum ModoExtraccion { Rapido, Profundo, Auto }

public record ProgresoLote(int Actual, int Total, string Archivo);
public record ResumenLote(int Total, int Ok, int Errores, bool Cancelado);

public class OrquestadorExtraccion(
    AppDbContext db,
    IEnumerable<IExtractorCad> extractores,
    IOptions<ConfiguracionExtraccion> opciones,
    ILogger<OrquestadorExtraccion> logger)
{
    private readonly ConfiguracionExtraccion _cfg = opciones.Value;

    // Procesa un único archivo por ID — para el botón "Extraer seleccionado" de la UI.
    public async Task ProcesarUnoAsync(int archivoId, ModoExtraccion modo, CancellationToken ct)
    {
        var archivo = await db.Archivos.FindAsync([archivoId], ct);
        if (archivo is null) return;
        archivo.EstadoRapido = "pendiente";
        await db.SaveChangesAsync(ct);
        await ProcesarArchivoAsync(archivo, modo, ct);
    }

    // carpetaFiltro: limita el lote a los archivos bajo esa carpeta (incluye subcarpetas —
    // es un prefijo de ruta, por eso extraer una subcarpeta del árbol del Explorador
    // "simplemente funciona"). null = todos los pendientes de la BD.
    public async Task<ResumenLote> ProcesarPendientesAsync(
        ModoExtraccion modo, CancellationToken ct, IProgress<ProgresoLote>? progreso = null,
        string? carpetaFiltro = null)
    {
        // Incluye también archivos con Rápida ya "ok" pero Profunda pendiente/fallida —
        // desde que EstadoRapido y EstadoProfundo se registran por separado (ver
        // ProcesarArchivoAsync), un fallo puramente profundo ya no degrada EstadoRapido,
        // así que hay que buscarlo explícitamente o el archivo nunca se reintenta.
        var consulta = db.Archivos
            .Where(a =>
                a.EstadoRapido == "pendiente" || a.EstadoRapido == "error" ||
                (a.EstadoRapido == "ok" &&
                    (a.EstadoProfundo == "pendiente" || a.EstadoProfundo == "error" || a.EstadoProfundo == "timeout")));

        if (!string.IsNullOrEmpty(carpetaFiltro))
        {
            // Límite de segmento: "C:\A" no debe capturar "C:\AB\..." — se filtra por
            // prefijo con separador (los archivos siempre tienen al menos "\nombre.ext").
            var prefijo = carpetaFiltro.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            consulta = consulta.Where(a => a.Ruta.StartsWith(prefijo));
        }

        var archivos = await consulta.ToListAsync(ct);

        logger.LogInformation("Orquestador: {Total} archivos a procesar (modo {Modo})", archivos.Count, modo);

        int ok = 0, errores = 0, procesados = 0;
        foreach (var archivo in archivos)
        {
            // Cancelación cooperativa entre archivos: lo ya procesado queda persistido,
            // el lote reporta cuánto alcanzó a hacer en vez de lanzar excepción.
            if (ct.IsCancellationRequested)
                return new ResumenLote(archivos.Count, ok, errores, Cancelado: true);

            procesados++;
            progreso?.Report(new ProgresoLote(procesados, archivos.Count, archivo.Nombre));
            try
            {
                await ProcesarArchivoAsync(archivo, modo, ct);
                if (archivo.EstadoRapido == "ok") ok++;
                else errores++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new ResumenLote(archivos.Count, ok, errores, Cancelado: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error no controlado procesando: {Ruta}", archivo.Ruta);
                archivo.EstadoRapido = "error";
                archivo.MensajeError = ex.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                errores++;
            }
        }

        return new ResumenLote(archivos.Count, ok, errores, Cancelado: false);
    }

    private async Task ProcesarArchivoAsync(Archivo archivo, ModoExtraccion modo, CancellationToken ct)
    {
        var alcance = DeterminarAlcance(archivo, modo);
        if (alcance == AlcanceExtraccion.Ninguno) return;

        // Candidatos: extractores cuyas capacidades cubren TODO el alcance pedido (superset),
        // no solo una intersección — así "Profunda" (features/roscas) solo la sirve SwApi,
        // mientras que "Rápida" la puede servir tanto DocManager (con licencia) como SwApi
        // (modo liviano, sin features/roscas — ver ExtractorSwApi.ExtraerAsync). Se prueban en
        // orden de registro; si el primero falla (p. ej. DocManager sin licencia) se reintenta
        // con el siguiente, permitiendo que SwApi sustituya a DocManager quedar sin licencia.
        var candidatos = extractores
            .Where(e => e.PuedeProcesar(archivo.Ruta) && (alcance & e.Capacidades) == alcance)
            .ToList();

        if (candidatos.Count == 0)
        {
            logger.LogWarning("Sin extractor disponible para: {Ruta}", archivo.Ruta);
            return;
        }

        var trabajo = new TrabajoExtraccion
        {
            ArchivoId     = archivo.Id,
            Tipo          = alcance.HasFlag(AlcanceExtraccion.Features) ? "profunda" : "rapida",
            Estado        = "en_proceso",
            FechaEncolado = DateTime.UtcNow.ToString("o"),
            FechaInicio   = DateTime.UtcNow.ToString("o"),
            Intentos      = 1
        };
        db.TrabajosExtraccion.Add(trabajo);
        await db.SaveChangesAsync(ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        ResultadoExtraccion resultado = new() { Estado = EstadoExtraccion.Error, MensajeError = "Sin extractor disponible" };
        foreach (var extractor in candidatos)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(_cfg.TimeoutPorArchivoSegundos));
                resultado = await extractor.ExtraerAsync(new SolicitudExtraccion(archivo.Ruta, alcance), cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                sw.Stop();
                logger.LogWarning("Timeout extrayendo con {Extractor}: {Ruta}", extractor.Nombre, archivo.Ruta);
                archivo.MensajeError = $"Timeout tras {_cfg.TimeoutPorArchivoSegundos} s ({extractor.Nombre})";
                // Un timeout en alcance Profunda no debe degradar un EstadoRapido ya "ok"
                // (el archivo sigue siendo válido a nivel rápido; solo falló el paso profundo).
                if (alcance.HasFlag(AlcanceExtraccion.Features)) archivo.EstadoProfundo = "timeout";
                else archivo.EstadoRapido = "timeout";
                await ActualizarTrabajoAsync(trabajo, "timeout", sw.ElapsedMilliseconds, archivo.MensajeError, ct);
                return;
            }

            if (resultado.Estado == EstadoExtraccion.Ok) break;

            logger.LogDebug("Extractor {Extractor} no pudo con {Ruta}: {Msg} — probando siguiente candidato",
                extractor.Nombre, archivo.Ruta, resultado.MensajeError);
        }
        sw.Stop();

        if (resultado.Estado == EstadoExtraccion.Ok)
        {
            await PersistirResultadoAsync(archivo, resultado, ct);
            archivo.EstadoRapido    = "ok";
            archivo.FechaExtrRapida = DateTime.UtcNow.ToString("o");
            // Alcance Profunda (features/roscas) exitoso: registrar también EstadoProfundo,
            // si no, DeterminarAlcanceAuto lo vuelve a pedir en cada ciclo para siempre
            // (EstadoProfundo nunca se marcaba "ok" y quedaba "pendiente" indefinidamente).
            if (alcance.HasFlag(AlcanceExtraccion.Features))
            {
                archivo.EstadoProfundo    = "ok";
                archivo.FechaExtrProfunda = DateTime.UtcNow.ToString("o");
            }
            archivo.MensajeError    = resultado.Advertencias.Count > 0
                ? string.Join("; ", resultado.Advertencias) : null;
            await ActualizarTrabajoAsync(trabajo, "ok", sw.ElapsedMilliseconds, null, ct);
            logger.LogInformation("Extraído OK: {Ruta}", archivo.Ruta);
        }
        else
        {
            var estadoBd = resultado.Estado switch
            {
                EstadoExtraccion.Timeout            => "timeout",
                EstadoExtraccion.VersionNoSoportada => "version_no_soportada",
                EstadoExtraccion.Bloqueado          => "bloqueado",
                EstadoExtraccion.Omitido            => "omitido",
                _                                   => "error"
            };
            // Igual que en el timeout: un alcance Profunda fallido no debe degradar un
            // EstadoRapido ya "ok" — el fallo pertenece al paso profundo, no al rápido.
            if (alcance.HasFlag(AlcanceExtraccion.Features)) archivo.EstadoProfundo = estadoBd;
            else archivo.EstadoRapido = estadoBd;
            archivo.MensajeError = resultado.MensajeError;
            await ActualizarTrabajoAsync(trabajo, estadoBd, sw.ElapsedMilliseconds, resultado.MensajeError, ct);
            logger.LogWarning("Extracción {Estado}: {Ruta} — {Msg}", estadoBd, archivo.Ruta, resultado.MensajeError);
        }

        await db.SaveChangesAsync(ct);
    }

    private AlcanceExtraccion DeterminarAlcance(Archivo archivo, ModoExtraccion modo) =>
        modo switch
        {
            ModoExtraccion.Rapido   => AlcanceExtraccion.Rapida,
            ModoExtraccion.Profundo => AlcanceExtraccion.Profunda,
            ModoExtraccion.Auto     => DeterminarAlcanceAuto(archivo),
            _                       => AlcanceExtraccion.Ninguno
        };

    private static AlcanceExtraccion DeterminarAlcanceAuto(Archivo archivo)
    {
        if (archivo.EstadoRapido != "ok")
            return AlcanceExtraccion.Rapida;

        // STEP: geometría importada sin árbol de features nativo — la extracción profunda
        // nunca aporta nada, y pedirla dejaría el archivo reintentando contra SW por siempre.
        if (archivo.Tipo == "step")
            return AlcanceExtraccion.Ninguno;

        bool necesitaProfunda =
            archivo.EstadoProfundo != "ok" ||
            (archivo.FechaExtrProfunda is not null && archivo.FechaModDisco is not null &&
             string.Compare(archivo.FechaExtrProfunda, archivo.FechaModDisco, StringComparison.Ordinal) < 0);

        return necesitaProfunda ? AlcanceExtraccion.Profunda : AlcanceExtraccion.Ninguno;
    }

    private async Task PersistirResultadoAsync(Archivo archivo, ResultadoExtraccion r, CancellationToken ct)
    {
        if (r.Archivo is not null)
        {
            archivo.VersionSw = r.Archivo.VersionSw;
            archivo.Autor     = r.Archivo.Autor;
            if (r.Archivo.PreviewPng is not null)
                archivo.RutaPreview = await GuardarPreviewAsync(archivo.Ruta, r.Archivo.PreviewPng, ct);
        }

        // Solo se reemplaza si el extractor aportó algo: una re-extracción que no genera
        // datos extra (p. ej. alcance sin Estructura) no debe borrar los existentes.
        if (r.DatosExtraJson is not null)
            archivo.DatosExtraJson = r.DatosExtraJson;

        // Configuraciones
        foreach (var dc in r.Configuraciones)
        {
            var cfg = await db.Configuraciones
                .FirstOrDefaultAsync(c => c.ArchivoId == archivo.Id && c.Nombre == dc.Nombre, ct);
            if (cfg is null)
                db.Configuraciones.Add(new Data.Entities.Configuracion
                    { ArchivoId = archivo.Id, Nombre = dc.Nombre, EsActiva = dc.EsActiva, EsDerivada = dc.EsDerivada });
            else
                (cfg.EsActiva, cfg.EsDerivada) = (dc.EsActiva, dc.EsDerivada);
        }
        await db.SaveChangesAsync(ct);

        // Propiedades
        foreach (var dp in r.Propiedades)
        {
            int? cfgId = dp.Configuracion is null ? null
                : (await db.Configuraciones.FirstOrDefaultAsync(
                    c => c.ArchivoId == archivo.Id && c.Nombre == dp.Configuracion, ct))?.Id;

            var prop = await db.Propiedades.FirstOrDefaultAsync(
                p => p.ArchivoId == archivo.Id && p.ConfiguracionId == cfgId && p.Nombre == dp.Nombre, ct);

            if (prop is null)
                db.Propiedades.Add(new Propiedad
                    { ArchivoId = archivo.Id, ConfiguracionId = cfgId, Nombre = dp.Nombre,
                      Valor = dp.Valor, ValorResuelto = dp.ValorResuelto, Tipo = dp.Tipo });
            else
                (prop.Valor, prop.ValorResuelto, prop.Tipo) = (dp.Valor, dp.ValorResuelto, dp.Tipo);
        }

        // Propiedades físicas
        foreach (var df in r.Fisicas)
        {
            var cfg = await db.Configuraciones
                .FirstOrDefaultAsync(c => c.ArchivoId == archivo.Id && c.Nombre == df.Configuracion, ct);
            if (cfg is null) continue;

            var fis = await db.PropiedadesFisicas.FirstOrDefaultAsync(
                pf => pf.ArchivoId == archivo.Id && pf.ConfiguracionId == cfg.Id, ct);
            if (fis is null)
                db.PropiedadesFisicas.Add(new PropiedadFisica
                    { ArchivoId = archivo.Id, ConfiguracionId = cfg.Id, Material = df.Material,
                      DensidadKgM3 = df.DensidadKgM3, MasaKg = df.MasaKg, VolumenM3 = df.VolumenM3, AreaM2 = df.AreaM2 });
            else
                (fis.Material, fis.DensidadKgM3, fis.MasaKg, fis.VolumenM3, fis.AreaM2) =
                    (df.Material, df.DensidadKgM3, df.MasaKg, df.VolumenM3, df.AreaM2);
        }

        // Componentes (delete+insert por config activa)
        if (r.Componentes.Count > 0)
        {
            var cfgActiva = await db.Configuraciones
                .FirstOrDefaultAsync(c => c.ArchivoId == archivo.Id && c.EsActiva, ct);
            if (cfgActiva is not null)
            {
                db.Componentes.RemoveRange(
                    db.Componentes.Where(c => c.EnsambleArchivoId == archivo.Id && c.EnsambleConfigId == cfgActiva.Id));
                await db.SaveChangesAsync(ct);

                foreach (var dc in r.Componentes)
                {
                    var hijo = await db.Archivos.FirstOrDefaultAsync(a => a.Ruta == dc.RutaReferenciada, ct);
                    if (hijo is null)
                        logger.LogWarning("Referencia rota en {Ensamble}: {Ruta}", archivo.Ruta, dc.RutaReferenciada);
                    db.Componentes.Add(new Componente
                    {
                        EnsambleArchivoId  = archivo.Id, EnsambleConfigId = cfgActiva.Id,
                        ComponenteArchivoId = hijo?.Id, RutaReferenciada = dc.RutaReferenciada,
                        ConfiguracionUsada = dc.ConfiguracionUsada, Cantidad = dc.Cantidad,
                        Suprimido = dc.Suprimido, EsToolbox = dc.EsToolbox, EsEnvelope = dc.EsEnvelope
                    });
                }
            }
        }

        // Features y Roscas (delete+insert — solo extracción profunda, Fase 2)
        if (r.Features.Count > 0)
        {
            db.Features.RemoveRange(db.Features.Where(f => f.ArchivoId == archivo.Id));
            await db.SaveChangesAsync(ct);

            foreach (var df in r.Features)
            {
                var feat = new Feature { ArchivoId = archivo.Id, Nombre = df.Nombre, TipoSw = df.TipoSw,
                    Categoria = df.Categoria, ParametrosJson = df.ParametrosJson,
                    Suprimido = df.Suprimido, Orden = df.Orden };
                db.Features.Add(feat);
                await db.SaveChangesAsync(ct);

                foreach (var dr in r.Roscas.Where(x => x.FeatureNombre == df.Nombre))
                    db.Roscas.Add(new Rosca { ArchivoId = archivo.Id, FeatureId = feat.Id,
                        Designacion = dr.Designacion, Estandar = dr.Estandar, TipoBarreno = dr.TipoBarreno,
                        DiametroNominalMm = dr.DiametroNominalMm, PasoMm = dr.PasoMm,
                        HilosPorPulgada = dr.HilosPorPulgada, ProfundidadRoscaMm = dr.ProfundidadRoscaMm,
                        ProfundidadBarrenoMm = dr.ProfundidadBarrenoMm, Pasante = dr.Pasante, Cantidad = dr.Cantidad });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> GuardarPreviewAsync(string rutaArchivo, byte[] png, CancellationToken ct)
    {
        try
        {
            var carpeta = Environment.ExpandEnvironmentVariables(_cfg.CarpetaCachePreviews);
            Directory.CreateDirectory(carpeta);
            var destino = Path.Combine(carpeta, Path.GetFileNameWithoutExtension(rutaArchivo) + ".png");
            await File.WriteAllBytesAsync(destino, png, ct);
            return destino;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo guardar preview de {Ruta}", rutaArchivo);
            return null;
        }
    }

    private async Task ActualizarTrabajoAsync(
        TrabajoExtraccion t, string estado, long ms, string? mensaje, CancellationToken ct)
    {
        t.Estado    = estado;
        t.FechaFin  = DateTime.UtcNow.ToString("o");
        t.DuracionMs = ms;
        t.Mensaje   = mensaje;
        await db.SaveChangesAsync(ct);
    }
}
