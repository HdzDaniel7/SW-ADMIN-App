namespace SWDataExtractor.Data.Entities;

public class Feature
{
    public int Id { get; set; }
    public int ArchivoId { get; set; }
    public required string Nombre { get; set; }
    public required string TipoSw { get; set; }
    public required string Categoria { get; set; }
    public string? ParametrosJson { get; set; }
    public bool Suprimido { get; set; }
    public int Orden { get; set; }

    public Archivo Archivo { get; set; } = null!;
    public ICollection<Rosca> Roscas { get; set; } = [];
}
