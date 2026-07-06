using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data;

namespace SWDataExtractor.Application.Servicios;

// ── DTOs de salida ────────────────────────────────────────────────────────────

// ConfiguracionUsada al final con default para no romper construcciones posicionales previas.
// En tornillería Toolbox el nombre de configuración lleva la designación ("M8x1.25 x 30").
public record ItemBom(
    int Nivel,
    int? ArchivoId,
    string Nombre,
    string Ruta,
    string Tipo,
    int CantidadEnPadre,
    bool EsToolbox,
    bool EsEnvelope,
    bool EsSuprimido,
    string? ConfiguracionUsada = null);

public record ItemBomAplanado(
    int? ArchivoId,
    string Nombre,
    string Ruta,
    string Tipo,
    int CantidadTotal,
    bool EsToolbox);

public record ItemWhereUsed(
    int EnsambleArchivoId,
    string EnsambleNombre,
    string EnsambleRuta,
    int Cantidad,
    string? ConfiguracionUsada);

public record DiffBom(
    string Ruta,
    string Nombre,
    string Cambio,          // "agregado" | "eliminado" | "cantidad_cambio"
    int? CantidadAntes,
    int? CantidadDespues);

// ── Servicio ──────────────────────────────────────────────────────────────────

public class ServicioBom(AppDbContext db)
{
    // Opciones de filtrado compartidas por BOM indentado y aplanado
    public record OpcionesBom(
        bool ExcluirToolbox    = false,
        bool ExcluirSuprimidos = true,
        bool ExcluirEnvelope   = true,
        int  ProfundidadMaxima = 50);

    // ── BOM indentado ─────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<ItemBom>> ObtenerBomIndentadoAsync(
        int ensambleArchivoId, OpcionesBom? opciones = null, CancellationToken ct = default)
    {
        opciones ??= new OpcionesBom();

        // Obtener configuración activa del ensamble raíz
        var cfgActivaId = await db.Configuraciones
            .Where(c => c.ArchivoId == ensambleArchivoId && c.EsActiva)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (cfgActivaId is null)
            return [];

        // CTE recursivo con SQL crudo (EF Core 8+ soporta FromSql con tipos primitivos/proyecciones)
        var sql = $"""
            WITH RECURSIVE bom(nivel, archivo_id, nombre, ruta, tipo, cantidad_en_padre, es_toolbox, es_envelope, es_suprimido, configuracion_usada) AS (
                SELECT 0, a.id, a.nombre, a.ruta, a.tipo, 1, 0, 0, 0, NULL
                FROM archivos a WHERE a.id = {ensambleArchivoId}

                UNION ALL

                SELECT
                    b.nivel + 1,
                    c.componente_archivo_id,
                    COALESCE(a.nombre, c.ruta_referenciada),
                    COALESCE(a.ruta,   c.ruta_referenciada),
                    COALESCE(a.tipo,   'otro'),
                    c.cantidad,
                    c.es_toolbox,
                    c.es_envelope,
                    c.suprimido,
                    c.configuracion_usada
                FROM bom b
                JOIN componentes c ON c.ensamble_archivo_id = b.archivo_id
                                   AND c.ensamble_config_id = {cfgActivaId}
                LEFT JOIN archivos a ON a.id = c.componente_archivo_id
                WHERE b.nivel < {opciones.ProfundidadMaxima}
                  {(opciones.ExcluirSuprimidos ? "AND c.suprimido = 0" : "")}
                  {(opciones.ExcluirToolbox    ? "AND c.es_toolbox  = 0" : "")}
                  {(opciones.ExcluirEnvelope   ? "AND c.es_envelope = 0" : "")}
            )
            SELECT nivel, archivo_id, nombre, ruta, tipo, cantidad_en_padre, es_toolbox, es_envelope, es_suprimido, configuracion_usada
            FROM bom ORDER BY nivel, nombre
            """;

        var rows = await db.Database
            .SqlQueryRaw<FilaBom>(sql)
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            // Componente no indexado en `archivos` (típico: tornillería del Toolbox, fuera de
            // las carpetas escaneadas): `nombre` trae la ruta completa referenciada — mostrar
            // solo el nombre de archivo; la ruta completa sigue disponible en `Ruta`.
            var nombre = r.archivo_id is null ? Path.GetFileName(r.nombre) : r.nombre;
            return new ItemBom(
                r.nivel, r.archivo_id, nombre, r.ruta, r.tipo,
                r.cantidad_en_padre, r.es_toolbox != 0, r.es_envelope != 0, r.es_suprimido != 0,
                r.configuracion_usada);
        }).ToList();
    }

    // ── BOM aplanado (cantidades totales acumuladas) ──────────────────────────
    public async Task<IReadOnlyList<ItemBomAplanado>> ObtenerBomAplanadoAsync(
        int ensambleArchivoId, OpcionesBom? opciones = null, CancellationToken ct = default)
    {
        var indentado = await ObtenerBomIndentadoAsync(ensambleArchivoId, opciones, ct);
        if (indentado.Count == 0) return [];

        // Recalcular cantidades totales multiplicando por el camino desde la raíz
        // Dado que el CTE da cantidad_en_padre (no acumulada), acumulamos con una pasada DFS
        var aplanado = new Dictionary<string, (ItemBom Item, int CantidadTotal)>(StringComparer.OrdinalIgnoreCase);

        // Construir árbol con stack para acumular multiplicadores
        var pilaMultiplicador = new Stack<(int Nivel, int Mult)>();
        pilaMultiplicador.Push((-1, 1));

        foreach (var item in indentado)
        {
            // Ajustar la pila para el nivel actual
            while (pilaMultiplicador.Peek().Nivel >= item.Nivel)
                pilaMultiplicador.Pop();

            var multAcum = pilaMultiplicador.Peek().Mult * item.CantidadEnPadre;
            pilaMultiplicador.Push((item.Nivel, multAcum));

            if (item.Nivel == 0) continue; // omitir raíz

            if (aplanado.TryGetValue(item.Ruta, out var existing))
                aplanado[item.Ruta] = (existing.Item, existing.CantidadTotal + multAcum);
            else
                aplanado[item.Ruta] = (item, multAcum);
        }

        return aplanado.Values
            .Select(v => new ItemBomAplanado(
                v.Item.ArchivoId, v.Item.Nombre, v.Item.Ruta, v.Item.Tipo,
                v.CantidadTotal, v.Item.EsToolbox))
            .OrderBy(i => i.Nombre)
            .ToList();
    }

    // ── Where-used (qué ensambles usan este archivo) ─────────────────────────
    public async Task<IReadOnlyList<ItemWhereUsed>> ObtenerWhereUsedAsync(
        int archivoId, CancellationToken ct = default)
    {
        return await db.Componentes
            .Where(c => c.ComponenteArchivoId == archivoId)
            .Join(db.Archivos, c => c.EnsambleArchivoId, a => a.Id,
                (c, a) => new ItemWhereUsed(a.Id, a.Nombre, a.Ruta, c.Cantidad, c.ConfiguracionUsada))
            .ToListAsync(ct);
    }

    // ── Diff entre dos ensambles (o dos momentos del mismo) ──────────────────
    public async Task<IReadOnlyList<DiffBom>> CompararBomAsync(
        int ensamble1Id, int ensamble2Id,
        OpcionesBom? opciones = null, CancellationToken ct = default)
    {
        var bom1 = (await ObtenerBomAplanadoAsync(ensamble1Id, opciones, ct))
            .ToDictionary(i => i.Ruta, StringComparer.OrdinalIgnoreCase);
        var bom2 = (await ObtenerBomAplanadoAsync(ensamble2Id, opciones, ct))
            .ToDictionary(i => i.Ruta, StringComparer.OrdinalIgnoreCase);

        var diff = new List<DiffBom>();

        foreach (var (ruta, item1) in bom1)
        {
            if (!bom2.TryGetValue(ruta, out var item2))
                diff.Add(new DiffBom(ruta, item1.Nombre, "eliminado", item1.CantidadTotal, null));
            else if (item1.CantidadTotal != item2.CantidadTotal)
                diff.Add(new DiffBom(ruta, item1.Nombre, "cantidad_cambio", item1.CantidadTotal, item2.CantidadTotal));
        }

        foreach (var (ruta, item2) in bom2)
            if (!bom1.ContainsKey(ruta))
                diff.Add(new DiffBom(ruta, item2.Nombre, "agregado", null, item2.CantidadTotal));

        return diff.OrderBy(d => d.Nombre).ToList();
    }

    // Proyección privada para el SqlQueryRaw (snake_case = columnas SQLite)
    private class FilaBom
    {
        public int nivel { get; set; }
        public int? archivo_id { get; set; }
        public string nombre { get; set; } = "";
        public string ruta { get; set; } = "";
        public string tipo { get; set; } = "";
        public int cantidad_en_padre { get; set; }
        public int es_toolbox { get; set; }
        public int es_envelope { get; set; }
        public int es_suprimido { get; set; }
        public string? configuracion_usada { get; set; }
    }
}
