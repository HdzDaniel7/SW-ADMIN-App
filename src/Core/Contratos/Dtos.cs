namespace SWDataExtractor.Core.Contratos;

public record SolicitudExtraccion(string Ruta, AlcanceExtraccion Alcance);

public record ResultadoExtraccion
{
    public required EstadoExtraccion Estado { get; init; }
    public string? MensajeError { get; init; }
    public DatosArchivo? Archivo { get; init; }
    public IReadOnlyList<DatosConfiguracion> Configuraciones { get; init; } = [];
    public IReadOnlyList<DatosPropiedad> Propiedades { get; init; } = [];
    public IReadOnlyList<DatosPropiedadesFisicas> Fisicas { get; init; } = [];
    public IReadOnlyList<DatosComponente> Componentes { get; init; } = [];
    public IReadOnlyList<DatosFeature> Features { get; init; } = [];
    public IReadOnlyList<DatosRosca> Roscas { get; init; } = [];
    public IReadOnlyList<string> Advertencias { get; init; } = [];

    // Datos que no encajan en el esquema fijo (DISENO §1b: datos nuevos → datos_extra_json
    // primero). Hoy lo usa ExtractorStep para el árbol de componentes internos ("bom_step").
    public string? DatosExtraJson { get; init; }
}

public record DatosArchivo(TipoArchivoCad Tipo, int? VersionSw, string? Autor, byte[]? PreviewPng);

public record DatosConfiguracion(string Nombre, bool EsActiva, bool EsDerivada);

public record DatosPropiedad(
    string? Configuracion,
    string Nombre,
    string? Valor,
    string? ValorResuelto,
    string Tipo);

public record DatosPropiedadesFisicas(
    string Configuracion,
    string? Material,
    double? DensidadKgM3,
    double? MasaKg,
    double? VolumenM3,
    double? AreaM2);

public record DatosComponente(
    string RutaReferenciada,
    string? ConfiguracionUsada,
    int Cantidad,
    bool Suprimido,
    bool EsToolbox,
    bool EsEnvelope);

public record DatosFeature(
    string Nombre,
    string TipoSw,
    string Categoria,
    string? ParametrosJson,
    bool Suprimido,
    int Orden);

public record DatosRosca(
    string FeatureNombre,
    string Designacion,
    string? Estandar,
    string TipoBarreno,
    double? DiametroNominalMm,
    double? PasoMm,
    double? HilosPorPulgada,
    double? ProfundidadRoscaMm,
    double? ProfundidadBarrenoMm,
    bool Pasante,
    int Cantidad);

// DTOs de escritura
public record LoteEscritura(string Usuario, IReadOnlyList<CambioPropiedad> Cambios);

public record CambioPropiedad(string Ruta, string? Configuracion, string Propiedad, string ValorNuevo);

public record ResultadoEscrituraLote(string LoteId, IReadOnlyList<ResultadoCambio> Resultados);

public record ResultadoCambio(
    CambioPropiedad Cambio,
    string? ValorAnterior,
    string Resultado,
    string? Mensaje);
