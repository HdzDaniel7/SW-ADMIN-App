using System.Text;
using System.Text.Json;
using SWDataExtractor.Core.Contratos;

namespace SWDataExtractor.Application.Servicios;

/// <summary>
/// Extractor liviano para archivos STEP (.stp / .step). Lee el texto del propio archivo
/// (ISO-10303-21) sin SolidWorks ni licencia DocManager (principio "instala y funciona"):
/// - Encabezado (FILE_DESCRIPTION, FILE_NAME, FILE_SCHEMA) → propiedades "STEP_*".
/// - Sección DATA: estructura de ensamble (PRODUCT + NEXT_ASSEMBLY_USAGE_OCCURRENCE) →
///   árbol de componentes internos en datos_extra_json (clave "bom_step"). Los componentes
///   viven DENTRO del STEP (no son archivos), por eso no van a la tabla `componentes`
///   (contaminarían referencias rotas y where-used).
/// Lo que un STEP NO trae y no puede extraerse: configuraciones, árbol de features/roscas
/// (solo guarda geometría B-rep) y masa/material fiables.
/// </summary>
public class ExtractorStep : IExtractorCad
{
    // El encabezado ISO-10303-21 vive al inicio del archivo y es pequeño; con 512 KB
    // sobra incluso para encabezados con descripciones largas, sin cargar archivos
    // de cientos de MB en memoria.
    private const int MaxBytesEncabezado = 512 * 1024;

    // Profundidad máxima del árbol de ensamble (corta ciclos degenerados).
    private const int MaxProfundidadBom = 32;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string Nombre => "StepHeader";

    // Declara Rapida para que el orquestador lo considere en modo Rápido/Auto (mismo
    // patrón que SwApi con Profunda): la capacidad indica qué alcances puede atender,
    // no que devuelva todos los datos (un STEP no tiene configuraciones ni físicas).
    public AlcanceExtraccion Capacidades => AlcanceExtraccion.Rapida;

    public bool PuedeProcesar(string ruta)
    {
        var ext = Path.GetExtension(ruta).ToLowerInvariant();
        return ext is ".stp" or ".step";
    }

    // Task.Run: el parseo de la sección DATA de un STEP grande toma segundos y este
    // extractor no tiene afinidad de hilo (a diferencia de los COM) — no bloquear la UI.
    public Task<ResultadoExtraccion> ExtraerAsync(SolicitudExtraccion solicitud, CancellationToken ct)
        => Task.Run(() => Extraer(solicitud, ct), ct);

    private static ResultadoExtraccion Extraer(SolicitudExtraccion solicitud, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var texto = LeerInicio(solicitud.Ruta);
            if (!texto.TrimStart().StartsWith("ISO-10303-21", StringComparison.Ordinal))
                return Error("El archivo no tiene encabezado STEP válido (falta la firma ISO-10303-21).");

            // Limitar al bloque HEADER: evita falsos positivos con texto de la sección DATA.
            int finHeader = texto.IndexOf("ENDSEC", StringComparison.Ordinal);
            if (finHeader > 0) texto = texto[..finHeader];

            var advertencias = new List<string>();
            var propiedades  = new List<DatosPropiedad>();
            string? autor    = null;

            // FILE_NAME(name, time_stamp, (author…), (organization…),
            //           preprocessor_version, originating_system, authorisation)
            var fileName = ParsearEntrada(texto, "FILE_NAME");
            if (fileName is null)
            {
                advertencias.Add("Encabezado STEP sin entrada FILE_NAME.");
            }
            else
            {
                AgregarProp(propiedades, "STEP_Nombre",        ComoTexto(fileName, 0));
                AgregarProp(propiedades, "STEP_Fecha",         ComoTexto(fileName, 1));
                AgregarProp(propiedades, "STEP_Autor",         ComoListaTexto(fileName, 2));
                AgregarProp(propiedades, "STEP_Organizacion",  ComoListaTexto(fileName, 3));
                AgregarProp(propiedades, "STEP_Preprocesador", ComoTexto(fileName, 4));
                AgregarProp(propiedades, "STEP_SistemaOrigen", ComoTexto(fileName, 5));
                AgregarProp(propiedades, "STEP_Autorizacion",  ComoTexto(fileName, 6));

                autor = PrimerTexto(fileName, 2);
            }

            // FILE_DESCRIPTION((description…), implementation_level)
            var fileDesc = ParsearEntrada(texto, "FILE_DESCRIPTION");
            if (fileDesc is not null)
                AgregarProp(propiedades, "STEP_Descripcion", ComoListaTexto(fileDesc, 0));

            // FILE_SCHEMA((schema…)) — indica el protocolo de aplicación (AP203/AP214/AP242).
            var fileSchema = ParsearEntrada(texto, "FILE_SCHEMA");
            var esquema    = fileSchema is not null ? PrimerTexto(fileSchema, 0) : null;
            if (!string.IsNullOrWhiteSpace(esquema))
                AgregarProp(propiedades, "STEP_Esquema", NombreAmigableEsquema(esquema));

            // ── Estructura de ensamble (sección DATA) ────────────────────────
            string? datosExtraJson = null;
            var tipo = TipoArchivoCad.Otro;
            if (solicitud.Alcance.HasFlag(AlcanceExtraccion.Estructura))
            {
                var bom = LeerEstructuraEnsamble(solicitud.Ruta, ct, advertencias);
                if (bom is not null)
                {
                    datosExtraJson = bom.Value.Json;
                    tipo           = TipoArchivoCad.Ensamble;
                    AgregarProp(propiedades, "STEP_Componentes",
                        bom.Value.TotalOcurrencias.ToString());
                }
            }

            return new ResultadoExtraccion
            {
                Estado         = EstadoExtraccion.Ok,
                Archivo        = new DatosArchivo(tipo, VersionSw: null, autor, PreviewPng: null),
                Propiedades    = propiedades,
                Advertencias   = advertencias,
                DatosExtraJson = datosExtraJson
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Error($"No se pudo leer el archivo STEP: {ex.Message}");
        }
    }

    private static ResultadoExtraccion Error(string mensaje) =>
        new() { Estado = EstadoExtraccion.Error, MensajeError = mensaje };

    private static string LeerInicio(string ruta)
    {
        using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Math.Min(MaxBytesEncabezado, fs.Length)];
        int leidos = fs.Read(buffer, 0, buffer.Length);
        // Latin-1: decodifica byte a byte sin lanzar por secuencias inválidas (los STEP
        // son ASCII según la norma, pero hay exportadores que cuelan bytes fuera de rango).
        return Encoding.Latin1.GetString(buffer, 0, leidos);
    }

    // ── Estructura de ensamble (ISO-10303-21, sección DATA) ─────────────────
    //
    // Cadena de entidades del estándar (AP203/AP214/AP242):
    //   NEXT_ASSEMBLY_USAGE_OCCURRENCE(id, nombre, descr, #padre, #hijo, $)  — 1 ocurrencia
    //   PRODUCT_DEFINITION(…, …, #formación, #contexto)
    //   PRODUCT_DEFINITION_FORMATION[_WITH_SPECIFIED_SOURCE](…, …, #producto)
    //   PRODUCT(id, nombre, descr, (#contextos))
    // Cada NAUO es UNA instancia; la cantidad es el número de NAUO con el mismo par
    // padre/hijo. Sin NAUO → el STEP es una pieza y no se genera nada.

    private sealed record NodoBomStep(string Nombre, int Cantidad, IReadOnlyList<NodoBomStep> Hijos);

    private static (string Json, int TotalOcurrencias)? LeerEstructuraEnsamble(
        string ruta, CancellationToken ct, List<string> advertencias)
    {
        var nombreProducto        = new Dictionary<string, string>(); // #producto  → nombre
        var productoDeFormacion   = new Dictionary<string, string>(); // #formación → #producto
        var formacionDeDefinicion = new Dictionary<string, string>(); // #definición→ #formación
        var ocurrencias           = new List<(string Padre, string Hijo)>();

        try
        {
            using var fs     = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var lector = new StreamReader(fs, Encoding.Latin1);

            var  sb     = new StringBuilder(256);
            bool enData = false;
            long chars  = 0;
            int  c;
            while ((c = lector.Read()) >= 0)
            {
                if (++chars % 1_000_000 == 0) ct.ThrowIfCancellationRequested();
                if (c != ';') { sb.Append((char)c); continue; }

                if (!enData)
                {
                    if (sb.ToString().AsSpan().Trim().SequenceEqual("DATA")) enData = true;
                    sb.Clear();
                    continue;
                }
                // Sentencia por sentencia: una que no parsee (p. ej. ';' dentro de un
                // nombre) se ignora sin tumbar el resto del archivo.
                try
                {
                    ProcesarSentencia(sb.ToString(), nombreProducto,
                        productoDeFormacion, formacionDeDefinicion, ocurrencias);
                }
                catch { /* sentencia malformada: continuar */ }
                sb.Clear();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            advertencias.Add($"No se pudo leer la estructura de ensamble: {ex.Message}");
            return null;
        }

        if (ocurrencias.Count == 0) return null; // pieza: sin estructura interna

        string? NombreDe(string definicion) =>
            formacionDeDefinicion.TryGetValue(definicion, out var f) &&
            productoDeFormacion.TryGetValue(f, out var p) &&
            nombreProducto.TryGetValue(p, out var n) ? n : null;

        var cantidadPorArista = ocurrencias
            .GroupBy(o => (o.Padre, o.Hijo))
            .ToDictionary(g => g.Key, g => g.Count());
        var hijosPorPadre = cantidadPorArista.Keys
            .GroupBy(k => k.Padre)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Hijo).ToList());
        var esHijo = ocurrencias.Select(o => o.Hijo).ToHashSet();

        NodoBomStep Nodo(string definicion, int cantidad, int profundidad)
        {
            List<NodoBomStep> hijos = profundidad < MaxProfundidadBom &&
                        hijosPorPadre.TryGetValue(definicion, out var subs)
                ? subs.Select(h => Nodo(h, cantidadPorArista[(definicion, h)], profundidad + 1)).ToList()
                : [];
            return new NodoBomStep(NombreDe(definicion) ?? "(sin nombre)", cantidad, hijos);
        }

        var raices = ocurrencias.Select(o => o.Padre).Distinct()
            .Where(p => !esHijo.Contains(p)).ToList();
        if (raices.Count == 0) raices = [ocurrencias[0].Padre]; // grafo cíclico: forzar entrada

        var arbol = raices
            .SelectMany(r => hijosPorPadre[r]
                .Select(h => Nodo(h, cantidadPorArista[(r, h)], 1)))
            .ToList();

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["bom_step"] = new
            {
                raiz              = NombreDe(raices[0]),
                total_ocurrencias = ocurrencias.Count,
                componentes       = arbol
            }
        }, OpcionesJson);

        return (json, ocurrencias.Count);
    }

    private static void ProcesarSentencia(
        string stmt,
        Dictionary<string, string> nombreProducto,
        Dictionary<string, string> productoDeFormacion,
        Dictionary<string, string> formacionDeDefinicion,
        List<(string Padre, string Hijo)> ocurrencias)
    {
        int eq = stmt.IndexOf('=');
        if (eq < 0) return;
        var id = stmt[..eq].Trim();
        if (!id.StartsWith('#')) return;

        int par = stmt.IndexOf('(', eq);
        if (par < 0) return;
        var entidad = stmt.AsSpan(eq + 1, par - eq - 1).Trim();

        // Los nombres de entidad van en mayúsculas por norma (ISO-10303-21 §6.4).
        if (entidad.SequenceEqual("PRODUCT"))
        {
            int i = par;
            var args = ParsearLista(stmt, ref i);
            var nombre = ComoTexto(args, 0) ?? ComoTexto(args, 1);
            if (nombre is not null) nombreProducto[id] = nombre;
        }
        else if (entidad.SequenceEqual("PRODUCT_DEFINITION_FORMATION") ||
                 entidad.SequenceEqual("PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE"))
        {
            int i = par;
            var args = ParsearLista(stmt, ref i);
            if (args.Count > 2 && args[2] is string prod && prod.StartsWith('#'))
                productoDeFormacion[id] = prod;
        }
        else if (entidad.SequenceEqual("PRODUCT_DEFINITION"))
        {
            int i = par;
            var args = ParsearLista(stmt, ref i);
            if (args.Count > 2 && args[2] is string form && form.StartsWith('#'))
                formacionDeDefinicion[id] = form;
        }
        else if (entidad.SequenceEqual("NEXT_ASSEMBLY_USAGE_OCCURRENCE"))
        {
            int i = par;
            var args = ParsearLista(stmt, ref i);
            if (args.Count > 4 && args[3] is string padre && args[4] is string hijo &&
                padre.StartsWith('#') && hijo.StartsWith('#'))
                ocurrencias.Add((padre, hijo));
        }
    }

    // ── Parseo del encabezado ────────────────────────────────────────────────

    /// <summary>Busca una entrada `PALABRA(args…)` y devuelve sus argumentos, o null si no está.</summary>
    private static List<object?>? ParsearEntrada(string texto, string palabra)
    {
        int desde = 0;
        while (true)
        {
            int idx = texto.IndexOf(palabra, desde, StringComparison.Ordinal);
            if (idx < 0) return null;

            int i = idx + palabra.Length;
            while (i < texto.Length && char.IsWhiteSpace(texto[i])) i++;
            if (i < texto.Length && texto[i] == '(')
                return ParsearLista(texto, ref i);

            desde = idx + palabra.Length; // coincidencia sin '(': seguir buscando
        }
    }

    /// <summary>
    /// Parsea una lista `(a, 'b', (c…))` de la sintaxis ISO-10303-21. Devuelve strings
    /// para valores con comillas, sublistas anidadas, null para `$`, y el token crudo
    /// para el resto (enteros, identificadores, referencias `#n`).
    /// </summary>
    private static List<object?> ParsearLista(string s, ref int i)
    {
        var lista = new List<object?>();
        i++; // saltar '('
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ')') { i++; break; }
            if (c == ',' || char.IsWhiteSpace(c)) { i++; continue; }
            // Comentarios /* … */ (ISO-10303-21 §6.1) — algunos exportadores anotan cada
            // campo del encabezado con ellos; no son argumentos.
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int fin = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = fin < 0 ? s.Length : fin + 2;
                continue;
            }
            if (c == '(') { lista.Add(ParsearLista(s, ref i)); continue; }
            if (c == '\'') { lista.Add(ParsearCadena(s, ref i)); continue; }

            int ini = i;
            while (i < s.Length && s[i] != ',' && s[i] != ')' && !char.IsWhiteSpace(s[i])) i++;
            var token = s[ini..i];
            lista.Add(token == "$" ? null : token);
        }
        return lista;
    }

    /// <summary>Parsea una cadena entre comillas simples; `''` es la comilla escapada.</summary>
    private static string ParsearCadena(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // saltar comilla inicial
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                i++;
                break;
            }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }

    // ── Utilidades de mapeo ──────────────────────────────────────────────────

    private static string? ComoTexto(List<object?> args, int idx) =>
        idx < args.Count && args[idx] is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static string? ComoListaTexto(List<object?> args, int idx)
    {
        if (idx >= args.Count) return null;
        var valores = args[idx] switch
        {
            List<object?> lista => lista.OfType<string>(),
            string s            => new[] { s },
            _                   => Enumerable.Empty<string>()
        };
        var texto = string.Join("; ", valores.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
        return texto.Length > 0 ? texto : null;
    }

    private static string? PrimerTexto(List<object?> args, int idx) =>
        idx < args.Count && args[idx] is List<object?> lista
            ? lista.OfType<string>().FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim()
            : ComoTexto(args, idx);

    private static void AgregarProp(List<DatosPropiedad> props, string nombre, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            props.Add(new DatosPropiedad(Configuracion: null, nombre, valor, valor, "texto"));
    }

    /// <summary>Antepone el protocolo de aplicación reconocible al nombre crudo del esquema.</summary>
    private static string NombreAmigableEsquema(string esquema)
    {
        var ap = esquema switch
        {
            _ when esquema.Contains("AUTOMOTIVE_DESIGN", StringComparison.OrdinalIgnoreCase)  => "AP214",
            _ when esquema.Contains("CONFIG_CONTROL",    StringComparison.OrdinalIgnoreCase)  => "AP203",
            _ when esquema.Contains("AP242",             StringComparison.OrdinalIgnoreCase)  => "AP242",
            _ => null
        };
        return ap is null ? esquema : $"{ap} — {esquema}";
    }
}
