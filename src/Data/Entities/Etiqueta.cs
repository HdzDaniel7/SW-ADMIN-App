namespace SWDataExtractor.Data.Entities;

public class Etiqueta
{
    public int Id { get; set; }
    public required string Nombre { get; set; }
    public string? Color { get; set; }
    public string? Descripcion { get; set; }
    public bool Activa { get; set; }

    public ICollection<ArchivoEtiqueta> ArchivoEtiquetas { get; set; } = [];
}
