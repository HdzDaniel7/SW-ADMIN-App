using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data;

namespace SWDataExtractor.Application.Servicios;

// ── DTOs (Fase 7a — ver DISENO.md §6) ────────────────────────────────────────

public record ArchivoResumen(
    int Id,
    string Nombre,
    string Ruta,
    string Tipo,
    string? FechaModDisco,   // formateada legible (dd/MM/yyyy HH:mm), no ISO crudo
    long? TamanoBytes,
    string EstadoRapido);

public record GrupoDuplicados(int NumeroGrupo, string HashSha256, IReadOnlyList<ArchivoResumen> Archivos);

public record ReferenciaRota(
    int EnsambleArchivoId,
    string EnsambleNombre,
    string EnsambleRuta,
    string RutaReferenciada,
    int Cantidad);

public record GrupoVersiones(int NumeroGrupo, string NombreBase, IReadOnlyList<ArchivoResumen> Versiones);

// Nodo del árbol de carpetas del explorador. Clase mutable solo durante la construcción.
public class NodoCarpeta(string ruta, string nombre)
{
    public string Ruta { get; } = ruta;
    public string Nombre { get; } = nombre;
    public List<NodoCarpeta> Hijas { get; } = [];
    public int TotalArchivos { get; internal set; }
}

public record SaludProyecto(
    int TotalComponentes,
    int Ok,
    int Pendientes,
    int ConError,
    int ReferenciasRotas,
    int Toolbox);

public record IncumplimientoPropiedad(int ArchivoId, string Nombre, string Ruta, string PropiedadFaltante);

// ── Servicio ──────────────────────────────────────────────────────────────────

// Fase 7a: explorador y reportes de proyecto. TODO se calcula sobre datos ya extraídos en la
// BD — cero llamadas a SolidWorks/DocManager, cero licencia (principio "instala y funciona").
public class ServicioAnalisisProyecto(AppDbContext db, EscaneadorCarpetas escaneador)
{
    // ── Explorador: árbol de carpetas derivado de archivos.ruta ──────────────
    public async Task<IReadOnlyList<NodoCarpeta>> ObtenerArbolCarpetasAsync(CancellationToken ct = default)
    {
        var rutas = await db.Archivos
            .Where(a => a.EstadoRapido != "omitido")
            .Select(a => a.Ruta)
            .ToListAsync(ct);

        var raices = await ObtenerRaicesAsync(ct);
        AgregarRaicesDerivadas(raices, rutas);
        var nodos  = new Dictionary<string, NodoCarpeta>(StringComparer.OrdinalIgnoreCase);
        var arbol  = new List<NodoCarpeta>();

        foreach (var ruta in rutas)
        {
            var dir = Path.GetDirectoryName(ruta);
            if (dir is null) continue;

            var raiz = raices.FirstOrDefault(r => EstaBajoCarpeta(dir, r)) ?? dir;

            // Crear la cadena de nodos desde la raíz hasta la carpeta del archivo,
            // incrementando el contador acumulado de cada nivel.
            NodoCarpeta? padre = null;
            var actual = raiz.TrimEnd(Path.DirectorySeparatorChar);
            var segmentosRestantes = dir.Length > actual.Length
                ? dir[(actual.Length + 1)..].Split(Path.DirectorySeparatorChar)
                : [];

            padre = ObtenerONuevo(actual, EtiquetaRaiz(actual), null);
            padre.TotalArchivos++;
            foreach (var seg in segmentosRestantes)
            {
                actual = actual + Path.DirectorySeparatorChar + seg;
                padre = ObtenerONuevo(actual, seg, padre);
                padre.TotalArchivos++;
            }
        }

        OrdenarRecursivo(arbol);
        return arbol;

        NodoCarpeta ObtenerONuevo(string ruta, string nombre, NodoCarpeta? padre)
        {
            if (nodos.TryGetValue(ruta, out var existente)) return existente;
            var nodo = new NodoCarpeta(ruta, nombre);
            nodos[ruta] = nodo;
            if (padre is null) arbol.Add(nodo);
            else padre.Hijas.Add(nodo);
            return nodo;
        }

        static string EtiquetaRaiz(string ruta) =>
            Path.GetFileName(ruta.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : ruta;

        static void OrdenarRecursivo(List<NodoCarpeta> nivel)
        {
            nivel.Sort((a, b) => string.Compare(a.Nombre, b.Nombre, StringComparison.OrdinalIgnoreCase));
            foreach (var n in nivel) OrdenarRecursivo(n.Hijas);
        }
    }

    // Archivos directamente dentro de la carpeta (sin incluir subcarpetas — el árbol ya las muestra).
    public async Task<IReadOnlyList<ArchivoResumen>> ObtenerArchivosDeCarpetaAsync(
        string carpeta, CancellationToken ct = default)
    {
        var prefijo = carpeta.TrimEnd(Path.DirectorySeparatorChar);
        var candidatos = await db.Archivos
            .Where(a => a.EstadoRapido != "omitido" && a.Ruta.StartsWith(prefijo))
            .ToListAsync(ct);

        return candidatos
            .Where(a => string.Equals(Path.GetDirectoryName(a.Ruta), prefijo, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(a => new ArchivoResumen(
                a.Id, a.Nombre, a.Ruta, a.Tipo, FormatearFecha(a.FechaModDisco), a.TamanoBytes, a.EstadoRapido))
            .ToList();
    }

    // ── Duplicados exactos (mismo hash, distinta ruta) ────────────────────────
    public async Task<IReadOnlyList<GrupoDuplicados>> ObtenerDuplicadosAsync(CancellationToken ct = default)
    {
        var archivos = await db.Archivos
            .Where(a => a.HashSha256 != null && a.EstadoRapido != "omitido")
            .ToListAsync(ct);

        int numero = 0;
        return archivos
            .GroupBy(a => a.HashSha256!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.First().TamanoBytes ?? 0)
            .Select(g => new GrupoDuplicados(++numero, g.Key,
                g.OrderBy(a => a.Ruta, StringComparer.OrdinalIgnoreCase)
                 .Select(a => new ArchivoResumen(a.Id, a.Nombre, a.Ruta, a.Tipo,
                     FormatearFecha(a.FechaModDisco), a.TamanoBytes, a.EstadoRapido))
                 .ToList()))
            .ToList();
    }

    // ── Referencias rotas (deuda del criterio de aceptación F1) ───────────────
    // Se excluye la tornillería Toolbox y los envelopes: sus rutas apuntan fuera de las
    // carpetas escaneadas por diseño (carpeta de SolidWorks), no son referencias "rotas".
    public async Task<IReadOnlyList<ReferenciaRota>> ObtenerReferenciasRotasAsync(CancellationToken ct = default)
    {
        // Proyección anónima en la query (el constructor de un record no es traducible a SQL)
        // y mapeo al DTO en memoria.
        var filas = await db.Componentes
            .Where(c => c.ComponenteArchivoId == null && !c.EsToolbox && !c.EsEnvelope)
            .Join(db.Archivos, c => c.EnsambleArchivoId, a => a.Id,
                (c, a) => new { a.Id, a.Nombre, a.Ruta, c.RutaReferenciada, c.Cantidad })
            .OrderBy(x => x.Nombre)
            .ToListAsync(ct);

        return filas
            .Where(x => !EsRutaToolbox(x.RutaReferenciada))
            .Select(x => new ReferenciaRota(x.Id, x.Nombre, x.Ruta, x.RutaReferenciada, x.Cantidad))
            .ToList();
    }

    // ── Posibles versiones del mismo archivo (heurística, ver DISENO.md §6) ──
    private static readonly Regex[] SufijosVersion =
    [
        new(@"[_\-\s]?v\d+$",                        RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[_\-\s]rev[\s_\-]?\w{1,3}$",           RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\s*\(\d+\)$",                          RegexOptions.Compiled),
        new(@"[_\-\s](final|old|nuevo|new|copia|copy|backup|bak)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[_\-\s]\d{4}[\-_]?\d{2}[\-_]?\d{2}$",  RegexOptions.Compiled),
    ];

    public async Task<IReadOnlyList<GrupoVersiones>> ObtenerPosiblesVersionesAsync(CancellationToken ct = default)
    {
        var archivos = await db.Archivos
            .Where(a => a.EstadoRapido != "omitido")
            .ToListAsync(ct);
        var raices = await ObtenerRaicesAsync(ct);

        int numero = 0;
        return archivos
            .GroupBy(a =>
            {
                var raiz = raices.FirstOrDefault(r => EstaBajoCarpeta(a.Ruta, r)) ?? "";
                return (Raiz: raiz.ToLowerInvariant(), a.Tipo, Base: NormalizarNombre(a.Nombre));
            })
            .Where(g => g.Count() > 1 && g.Select(a => a.HashSha256).Distinct().Count() > 1)
            .OrderBy(g => g.Key.Base)
            .Select(g => new GrupoVersiones(++numero, g.Key.Base,
                g.OrderByDescending(a => a.FechaModDisco, StringComparer.Ordinal) // ISO ordena cronológico
                 .Select(a => new ArchivoResumen(a.Id, a.Nombre, a.Ruta, a.Tipo,
                     FormatearFecha(a.FechaModDisco), a.TamanoBytes, a.EstadoRapido))
                 .ToList()))
            .ToList();
    }

    // Quita extensión y sufijos comunes de versionado, iterando hasta estabilizar
    // ("Soporte_v2_final" → "soporte"). Los patrones son ajustables sin romper contrato.
    public static string NormalizarNombre(string nombreArchivo)
    {
        var nombre = Path.GetFileNameWithoutExtension(nombreArchivo).Trim().ToLowerInvariant();
        bool cambio = true;
        while (cambio && nombre.Length > 0)
        {
            cambio = false;
            foreach (var regex in SufijosVersion)
            {
                var nuevo = regex.Replace(nombre, "").Trim();
                if (nuevo != nombre && nuevo.Length > 0)
                {
                    nombre = nuevo;
                    cambio = true;
                }
            }
        }
        return nombre;
    }

    // ── Dashboard de proyecto ─────────────────────────────────────────────────

    // Ensambles top-level: los que no aparecen como componente hijo de ningún otro ensamble.
    public async Task<IReadOnlyList<ArchivoResumen>> ObtenerEnsamblesTopLevelAsync(CancellationToken ct = default)
    {
        var idsHijos = db.Componentes
            .Where(c => c.ComponenteArchivoId != null)
            .Select(c => c.ComponenteArchivoId!.Value);

        return await db.Archivos
            .Where(a => a.Tipo == "ensamble" && a.EstadoRapido != "omitido" && !idsHijos.Contains(a.Id))
            .OrderBy(a => a.Nombre)
            .Select(a => new ArchivoResumen(a.Id, a.Nombre, a.Ruta, a.Tipo,
                null, a.TamanoBytes, a.EstadoRapido))
            .ToListAsync(ct);
    }

    // Salud del proyecto a partir del BOM indentado ya calculable (ServicioBom):
    // el llamador pasa los items para no recalcular el CTE dos veces en la misma pantalla.
    public async Task<SaludProyecto> CalcularSaludAsync(
        IReadOnlyList<ItemBom> bom, CancellationToken ct = default)
    {
        var ids = bom.Where(i => i.Nivel > 0 && i.ArchivoId is not null)
                     .Select(i => i.ArchivoId!.Value)
                     .Distinct()
                     .ToList();

        var estados = await db.Archivos
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.EstadoRapido })
            .ToDictionaryAsync(a => a.Id, a => a.EstadoRapido, ct);

        int ok = 0, pendientes = 0, conError = 0, rotas = 0, toolbox = 0;
        foreach (var item in bom.Where(i => i.Nivel > 0))
        {
            if (item.ArchivoId is null)
            {
                if (item.EsToolbox) toolbox++;
                else rotas++;
                continue;
            }
            switch (estados.GetValueOrDefault(item.ArchivoId.Value))
            {
                case "ok":        ok++; break;
                case "pendiente": pendientes++; break;
                default:          conError++; break;
            }
        }

        return new SaludProyecto(bom.Count(i => i.Nivel > 0), ok, pendientes, conError, rotas, toolbox);
    }

    // ── Cumplimiento del diccionario de propiedades ───────────────────────────
    // Qué archivos (ya extraídos OK) no tienen valor para las propiedades marcadas como
    // obligatorias en diccionario_propiedades. Los pendientes/con error se excluyen: aún
    // no tienen propiedades extraídas, señalarlos sería ruido.
    public async Task<IReadOnlyList<IncumplimientoPropiedad>> ObtenerIncumplimientosAsync(
        CancellationToken ct = default)
    {
        var obligatorias = await db.DiccionarioPropiedades
            .Where(d => d.Activa && d.Obligatoria)
            .Select(d => d.Nombre)
            .ToListAsync(ct);
        if (obligatorias.Count == 0) return [];

        var archivos = await db.Archivos
            .Where(a => a.EstadoRapido == "ok" && (a.Tipo == "pieza" || a.Tipo == "ensamble"))
            .Select(a => new { a.Id, a.Nombre, a.Ruta })
            .ToListAsync(ct);

        // Propiedades con valor (a nivel documento o configuración) por archivo.
        var conValor = await db.Propiedades
            .Where(p => p.ValorResuelto != null && p.ValorResuelto != "")
            .Select(p => new { p.ArchivoId, p.Nombre })
            .Distinct()
            .ToListAsync(ct);
        var porArchivo = conValor
            .GroupBy(p => p.ArchivoId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var resultado = new List<IncumplimientoPropiedad>();
        foreach (var a in archivos)
        {
            var presentes = porArchivo.GetValueOrDefault(a.Id);
            foreach (var propiedad in obligatorias)
                if (presentes is null || !presentes.Contains(propiedad))
                    resultado.Add(new IncumplimientoPropiedad(a.Id, a.Nombre, a.Ruta, propiedad));
        }
        return resultado.OrderBy(r => r.Nombre).ThenBy(r => r.PropiedadFaltante).ToList();
    }

    // ── Auxiliares ────────────────────────────────────────────────────────────

    // Heurística: la extracción vía SwApi todavía no marca es_toolbox (la detección COM está
    // pendiente de validar con SW real), así que la tornillería del Toolbox aparecería como
    // "referencia rota" al vivir fuera de las carpetas escaneadas. Se detecta por la ruta
    // estándar del Toolbox hasta que la extracción la marque de forma confiable.
    private static bool EsRutaToolbox(string ruta) =>
        ruta.Contains(@"\SOLIDWORKS Data\", StringComparison.OrdinalIgnoreCase) ||
        ruta.Contains(@"\Toolbox\", StringComparison.OrdinalIgnoreCase) ||
        ruta.Contains(@"\browser\", StringComparison.OrdinalIgnoreCase);

    // Coincidencia de prefijo con límite de segmento: "C:\A" contiene "C:\A\x" pero NO "C:\AB\x".
    private static bool EstaBajoCarpeta(string ruta, string carpeta) =>
        ruta.Equals(carpeta, StringComparison.OrdinalIgnoreCase) ||
        ruta.StartsWith(carpeta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Para archivos fuera de las raíces configuradas (o sin raíces guardadas): deriva las
    // carpetas raíz mínimas del propio conjunto — un directorio es raíz si ningún otro
    // directorio del conjunto es su ancestro (evita que cada subcarpeta sea raíz aparte).
    private static void AgregarRaicesDerivadas(List<string> raices, List<string> rutas)
    {
        var dirs = rutas
            .Select(Path.GetDirectoryName)
            .Where(d => d is not null)
            .Select(d => d!.TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(d => !raices.Any(r => EstaBajoCarpeta(d, r)))
            .ToList();

        raices.AddRange(dirs.Where(d =>
            !dirs.Any(otro => !otro.Equals(d, StringComparison.OrdinalIgnoreCase) && EstaBajoCarpeta(d, otro))));
    }

    private async Task<List<string>> ObtenerRaicesAsync(CancellationToken ct)
    {
        var raices = await escaneador.ObtenerCarpetasGuardadasAsync(ct);
        return raices.Select(r => r.TrimEnd(Path.DirectorySeparatorChar)).ToList();
    }

    internal static string? FormatearFecha(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var fecha)
            ? fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : iso;
    }
}
