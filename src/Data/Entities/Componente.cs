namespace SWDataExtractor.Data.Entities;

public class Componente
{
    public int Id { get; set; }
    public int EnsambleArchivoId { get; set; }
    public int EnsambleConfigId { get; set; }
    public int? ComponenteArchivoId { get; set; }
    public required string RutaReferenciada { get; set; }
    public string? ConfiguracionUsada { get; set; }
    public int Cantidad { get; set; }
    public bool Suprimido { get; set; }
    public bool EsToolbox { get; set; }
    public bool EsEnvelope { get; set; }
    public string? DatosExtraJson { get; set; }

    public Archivo EnsambleArchivo { get; set; } = null!;
    public Configuracion EnsambleConfig { get; set; } = null!;
    public Archivo? ComponenteArchivo { get; set; }
}
