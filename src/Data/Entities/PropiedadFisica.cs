namespace SWDataExtractor.Data.Entities;

public class PropiedadFisica
{
    public int Id { get; set; }
    public int ArchivoId { get; set; }
    public int ConfiguracionId { get; set; }
    public string? Material { get; set; }
    public double? DensidadKgM3 { get; set; }
    public double? MasaKg { get; set; }
    public double? VolumenM3 { get; set; }
    public double? AreaM2 { get; set; }

    public Archivo Archivo { get; set; } = null!;
    public Configuracion Configuracion { get; set; } = null!;
}
