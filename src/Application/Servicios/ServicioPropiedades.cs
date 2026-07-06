using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.Application.Servicios;

// ── DTOs de lectura ───────────────────────────────────────────────────────────

// Componente: nombre del archivo de origen cuando la fila viene de un componente del BOM
// (modo "incluir componentes" del detalle de ensambles); null para el archivo propio.
// Parámetro opcional al final para no romper construcciones posicionales existentes.
public record PropiedadVista(
    string Nombre,
    string? Configuracion,
    string? Valor,
    string? ValorResuelto,
    string? Tipo,
    bool EsEstandar,
    bool EsObligatoria,
    string? NivelEstandar,
    string? Componente = null);

public record ErrorValidacion(string Nombre, string Descripcion);

public record ResultadoValidacion(
    IReadOnlyList<PropiedadVista> Propiedades,
    IReadOnlyList<ErrorValidacion> Errores);

public record PreviewLoteEscritura(
    IReadOnlyList<CambioPropiedad> Cambios,
    IReadOnlyList<string> Advertencias);

// ── Servicio ──────────────────────────────────────────────────────────────────

public class ServicioPropiedades(AppDbContext db, IEscritorPropiedades escritor)
{
    // Lectura tabulada: propiedades del archivo (doc + todas las configs)
    public async Task<IReadOnlyList<PropiedadVista>> LeerPropiedadesAsync(
        int archivoId, CancellationToken ct = default)
    {
        var propiedades = await db.Propiedades
            .Where(p => p.ArchivoId == archivoId)
            .Include(p => p.Configuracion)
            .ToListAsync(ct);

        var diccionario = await db.DiccionarioPropiedades
            .Where(d => d.Activa)
            .ToDictionaryAsync(d => d.Nombre, StringComparer.OrdinalIgnoreCase, ct);

        return propiedades.Select(p =>
        {
            diccionario.TryGetValue(p.Nombre, out var def);
            return new PropiedadVista(
                p.Nombre,
                p.Configuracion?.Nombre,
                p.Valor,
                p.ValorResuelto,
                p.Tipo,
                def is not null,
                def?.Obligatoria ?? false,
                def?.Nivel);
        }).ToList();
    }

    // Validación contra diccionario: detecta obligatorias faltantes y valores inválidos
    public async Task<ResultadoValidacion> ValidarPropiedadesAsync(
        int archivoId, CancellationToken ct = default)
    {
        var propiedades = await LeerPropiedadesAsync(archivoId, ct);
        var obligatorias = await db.DiccionarioPropiedades
            .Where(d => d.Activa && d.Obligatoria)
            .ToListAsync(ct);

        var errores = new List<ErrorValidacion>();
        var nombresPresentes = propiedades.Select(p => p.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var def in obligatorias)
        {
            if (!nombresPresentes.Contains(def.Nombre))
                errores.Add(new ErrorValidacion(def.Nombre, "Propiedad obligatoria ausente"));
        }

        foreach (var prop in propiedades)
        {
            var desc = await EsValorValidoAsync(prop, ct);
            if (desc is not null)
                errores.Add(new ErrorValidacion(prop.Nombre, desc));
        }

        return new ResultadoValidacion(propiedades, errores);
    }

    // Prepara un lote mostrando valor anterior vs nuevo (diff previo para confirmación en UI)
    public async Task<PreviewLoteEscritura> PrepararLoteAsync(
        IReadOnlyList<CambioPropiedad> cambiosSolicitados, CancellationToken ct = default)
    {
        var cambiosFiltrados = new List<CambioPropiedad>();
        var advertencias     = new List<string>();

        foreach (var c in cambiosSolicitados)
        {
            var archivo = await db.Archivos.FirstOrDefaultAsync(a => a.Ruta == c.Ruta, ct);
            if (archivo is null)
            {
                advertencias.Add($"Archivo no encontrado en BD: {c.Ruta}");
                continue;
            }

            int? cfgId = c.Configuracion is null ? null
                : (await db.Configuraciones.FirstOrDefaultAsync(
                    cfg => cfg.ArchivoId == archivo.Id && cfg.Nombre == c.Configuracion, ct))?.Id;

            var actual = await db.Propiedades.FirstOrDefaultAsync(
                p => p.ArchivoId == archivo.Id && p.ConfiguracionId == cfgId && p.Nombre == c.Propiedad, ct);

            if (actual?.Valor == c.ValorNuevo)
            {
                advertencias.Add($"{Path.GetFileName(c.Ruta)}/{c.Propiedad}: valor sin cambio, omitido");
                continue;
            }

            cambiosFiltrados.Add(c);
        }

        return new PreviewLoteEscritura(cambiosFiltrados, advertencias);
    }

    // Ejecuta la escritura real a través del IEscritorPropiedades (DocManager)
    // y persiste el historial de auditoría en BD.
    public async Task<ResultadoEscrituraLote> EscribirLoteAsync(
        LoteEscritura lote, CancellationToken ct = default)
    {
        var resultado = await escritor.EscribirAsync(lote, ct);

        // Persistir historial
        foreach (var r in resultado.Resultados)
        {
            var archivo = await db.Archivos
                .FirstOrDefaultAsync(a => a.Ruta == r.Cambio.Ruta, ct);
            if (archivo is null) continue;

            db.HistorialPropiedades.Add(new HistorialPropiedad
            {
                LoteId        = resultado.LoteId,
                ArchivoId     = archivo.Id,
                Configuracion = r.Cambio.Configuracion,
                Propiedad     = r.Cambio.Propiedad,
                ValorAnterior = r.ValorAnterior,
                ValorNuevo    = r.Cambio.ValorNuevo,
                Usuario       = lote.Usuario,
                Fecha         = DateTime.UtcNow.ToString("o"),
                Resultado     = r.Resultado
            });
        }

        await db.SaveChangesAsync(ct);
        return resultado;
    }

    private async Task<string?> EsValorValidoAsync(PropiedadVista prop, CancellationToken ct)
    {
        if (!prop.EsEstandar) return null;

        var def = await db.DiccionarioPropiedades
            .FirstOrDefaultAsync(d => d.Nombre == prop.Nombre, ct);
        if (def is null) return null;

        if (def.Tipo == "lista" && def.ValoresPermitidosJson is not null)
        {
            var permitidos = System.Text.Json.JsonSerializer
                .Deserialize<List<string>>(def.ValoresPermitidosJson) ?? [];
            if (prop.ValorResuelto is not null &&
                !permitidos.Contains(prop.ValorResuelto, StringComparer.OrdinalIgnoreCase))
                return $"Valor '{prop.ValorResuelto}' no está en la lista permitida";
        }

        return null;
    }
}
