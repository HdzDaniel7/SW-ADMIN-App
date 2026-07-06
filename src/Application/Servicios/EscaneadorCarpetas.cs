using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.Application.Servicios;

public record ResultadoEscaneo(int Nuevos, int Actualizados, int SinCambios, int Eliminados, int Errores);

public class EscaneadorCarpetas(
    AppDbContext db,
    IOptions<ConfiguracionExtraccion> opciones,
    ILogger<EscaneadorCarpetas> logger)
{
    private readonly ConfiguracionExtraccion _cfg = opciones.Value;

    private const string ClaveCarpetas = "CarpetasEscaneo";

    private async Task<IReadOnlyList<string>> ObtenerCarpetasAsync(CancellationToken ct)
    {
        var carpetas = await ObtenerCarpetasGuardadasAsync(ct);
        if (carpetas.Count > 0)
        {
            logger.LogInformation("Carpetas de escaneo leídas desde BD: {N}", carpetas.Count);
            return carpetas;
        }
        logger.LogInformation("Usando carpetas de appsettings.json: {N}", _cfg.CarpetasRaiz.Count);
        return _cfg.CarpetasRaiz;
    }

    /// Lista de carpetas guardada en ajustes_app tal cual (sin fallback a appsettings.json).
    /// Usada por la UI de configuración para editar/comparar contra el valor persistido.
    public async Task<List<string>> ObtenerCarpetasGuardadasAsync(CancellationToken ct)
    {
        var ajuste = await db.AjustesApp.FirstOrDefaultAsync(a => a.Clave == ClaveCarpetas, ct);
        if (string.IsNullOrEmpty(ajuste?.Valor)) return [];
        return JsonSerializer.Deserialize<List<string>>(ajuste.Valor) ?? [];
    }

    public async Task GuardarCarpetasAsync(List<string> carpetas, CancellationToken ct)
    {
        var json    = JsonSerializer.Serialize(carpetas);
        var ajuste  = await db.AjustesApp.FirstOrDefaultAsync(a => a.Clave == ClaveCarpetas, ct);
        if (ajuste is null)
        {
            db.AjustesApp.Add(new AjusteApp
            {
                Clave       = ClaveCarpetas,
                Valor       = json,
                Descripcion = "Carpetas raíz de escaneo configuradas desde la UI"
            });
        }
        else
        {
            ajuste.Valor = json;
        }
        await db.SaveChangesAsync(ct);
    }

    /// Elimina de la BD (cascada EF) los archivos indexados cuya ruta está dentro de alguna
    /// de las carpetas dadas. Usado cuando el usuario quita carpetas de la lista de escaneo
    /// y quiere purgar sus registros en vez de dejarlos marcados "omitido".
    public async Task<int> BorrarArchivosBajoCarpetasAsync(IEnumerable<string> carpetas, CancellationToken ct)
    {
        var prefijos = carpetas
            .Select(c => c.TrimEnd('\\', '/') + Path.DirectorySeparatorChar)
            .ToList();
        if (prefijos.Count == 0) return 0;

        var candidatos = await db.Archivos.Select(a => new { a.Id, a.Ruta }).ToListAsync(ct);
        var idsABorrar = candidatos
            .Where(a => prefijos.Any(p => a.Ruta.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Select(a => a.Id)
            .ToList();
        if (idsABorrar.Count == 0) return 0;

        var archivos = await db.Archivos.Where(a => idsABorrar.Contains(a.Id)).ToListAsync(ct);
        db.Archivos.RemoveRange(archivos);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Purgados {N} archivo(s) de la BD por remoción de carpeta(s)", archivos.Count);
        return archivos.Count;
    }

    /// Elimina de la BD todos los archivos actualmente marcados "omitido" (ya no encontrados
    /// en disco), sin importar la causa. Mantenimiento manual disparado desde la UI.
    public async Task<int> PurgarOmitidosAsync(CancellationToken ct)
    {
        var omitidos = await db.Archivos.Where(a => a.EstadoRapido == "omitido").ToListAsync(ct);
        if (omitidos.Count == 0) return 0;

        db.Archivos.RemoveRange(omitidos);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Purgados {N} archivo(s) omitido(s) de la BD", omitidos.Count);
        return omitidos.Count;
    }

    public async Task<ResultadoEscaneo> EscanearAsync(CancellationToken ct)
    {
        int nuevos = 0, actualizados = 0, sinCambios = 0, eliminados = 0, errores = 0;

        var archivosEnDisco = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var carpetasRaiz = await ObtenerCarpetasAsync(ct);
        foreach (var carpeta in carpetasRaiz)
        {
            if (!Directory.Exists(carpeta))
            {
                logger.LogWarning("Carpeta raíz no existe: {Carpeta}", carpeta);
                continue;
            }

            IEnumerable<string> rutas;
            try
            {
                rutas = Directory.EnumerateFiles(carpeta, "*.*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error enumerando carpeta: {Carpeta}", carpeta);
                errores++;
                continue;
            }

            foreach (var ruta in rutas)
            {
                if (!_cfg.ExtensionesIncluidas.Contains(
                        Path.GetExtension(ruta), StringComparer.OrdinalIgnoreCase))
                    continue;
                if (EstaExcluida(ruta))
                    continue;
                archivosEnDisco.Add(Path.GetFullPath(ruta));
            }
        }

        foreach (var ruta in archivosEnDisco)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (esNuevo, fueActualizado) = await UpsertAsync(ruta, ct);
                if (esNuevo) nuevos++;
                else if (fueActualizado) actualizados++;
                else sinCambios++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando archivo: {Ruta}", ruta);
                errores++;
            }
        }

        // Detectar eliminados (en BD pero no en disco → marcar omitido, no borrar)
        var rutasEnBd = await db.Archivos
            .Where(a => a.EstadoRapido != "omitido")
            .Select(a => a.Ruta)
            .ToListAsync(ct);

        foreach (var ruta in rutasEnBd.Where(r => !archivosEnDisco.Contains(r)))
        {
            var archivo = await db.Archivos.FirstAsync(a => a.Ruta == ruta, ct);
            archivo.EstadoRapido   = "omitido";
            archivo.EstadoProfundo = "omitido";
            archivo.MensajeError   = "Archivo no encontrado en disco al escanear";
            eliminados++;
            logger.LogInformation("Archivo ya no existe en disco (marcado omitido): {Ruta}", ruta);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Escaneo — Nuevos: {N} | Actualizados: {A} | Sin cambios: {S} | Eliminados: {E} | Errores: {Err}",
            nuevos, actualizados, sinCambios, eliminados, errores);

        return new ResultadoEscaneo(nuevos, actualizados, sinCambios, eliminados, errores);
    }

    private async Task<(bool EsNuevo, bool FueActualizado)> UpsertAsync(string ruta, CancellationToken ct)
    {
        var info     = new FileInfo(ruta);
        var fechaMod = info.LastWriteTimeUtc.ToString("o");
        var hash     = ComputarHash(ruta);

        var existente = await db.Archivos.FirstOrDefaultAsync(a => a.Ruta == ruta, ct);

        if (existente is null)
        {
            db.Archivos.Add(new Archivo
            {
                Ruta           = ruta,
                Nombre         = info.Name,
                Tipo           = DetectarTipo(ruta),
                HashSha256     = hash,
                TamanoBytes    = info.Length,
                FechaModDisco  = fechaMod,
                EstadoRapido   = "pendiente",
                EstadoProfundo = "pendiente",
                Origen         = "sistema_archivos"
            });
            logger.LogDebug("Nuevo archivo: {Ruta}", ruta);
            return (true, false);
        }

        // Si estaba marcado omitido, restaurarlo aunque no haya cambiado
        if (existente.EstadoRapido == "omitido")
        {
            existente.HashSha256    = hash;
            existente.TamanoBytes   = info.Length;
            existente.FechaModDisco = fechaMod;
            existente.EstadoRapido  = "pendiente";
            existente.EstadoProfundo = existente.EstadoProfundo == "omitido" ? "pendiente" : existente.EstadoProfundo;
            existente.MensajeError  = null;
            logger.LogInformation("Archivo restaurado (estaba omitido): {Ruta}", ruta);
            return (false, true);
        }

        bool cambio = existente.HashSha256 != hash || existente.FechaModDisco != fechaMod;
        if (cambio)
        {
            existente.HashSha256    = hash;
            existente.TamanoBytes   = info.Length;
            existente.FechaModDisco = fechaMod;
            existente.EstadoRapido  = "pendiente";
            existente.MensajeError  = null;
            logger.LogDebug("Archivo modificado, marcado para reextracción: {Ruta}", ruta);
            return (false, true);
        }

        return (false, false);
    }

    private static string DetectarTipo(string ruta) =>
        Path.GetExtension(ruta).ToLowerInvariant() switch
        {
            ".sldprt"          => "pieza",
            ".sldasm"          => "ensamble",
            ".slddrw"          => "plano",
            ".stp" or ".step"  => "step",
            _                  => "otro"
        };

    private static string ComputarHash(string ruta)
    {
        // FileShare.ReadWrite permite leer aunque SW (u otra app) tenga el archivo abierto con lock de escritura.
        using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private bool EstaExcluida(string ruta)
    {
        var nombre = Path.GetFileName(ruta);
        foreach (var patron in _cfg.PatronesExcluidos)
            if (CoincidenGlob(nombre, patron) || CoincidenGlob(ruta, patron))
                return true;
        return false;
    }

    private static bool CoincidenGlob(string texto, string patron)
    {
        var regex = "^" + Regex.Escape(patron).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(texto, regex, RegexOptions.IgnoreCase);
    }
}
