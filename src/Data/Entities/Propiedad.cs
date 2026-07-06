namespace SWDataExtractor.Data.Entities;

public class Propiedad
{
    public int Id { get; set; }
    public int ArchivoId { get; set; }
    public int? ConfiguracionId { get; set; }
    public required string Nombre { get; set; }
    public string? Valor { get; set; }
    public string? ValorResuelto { get; set; }
    public string? Tipo { get; set; }

    public Archivo Archivo { get; set; } = null!;
    public Configuracion? Configuracion { get; set; }
}
