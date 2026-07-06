namespace SWDataExtractor.Data.Entities;

public class Rosca
{
    public int Id { get; set; }
    public int ArchivoId { get; set; }
    public int FeatureId { get; set; }
    public required string Designacion { get; set; }
    public string? Estandar { get; set; }
    public string? TipoBarreno { get; set; }
    public double? DiametroNominalMm { get; set; }
    public double? PasoMm { get; set; }
    public double? HilosPorPulgada { get; set; }
    public double? ProfundidadRoscaMm { get; set; }
    public double? ProfundidadBarrenoMm { get; set; }
    public bool Pasante { get; set; }
    public int Cantidad { get; set; }

    public Archivo Archivo { get; set; } = null!;
    public Feature Feature { get; set; } = null!;
}
