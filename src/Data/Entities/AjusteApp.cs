namespace SWDataExtractor.Data.Entities;

public class AjusteApp
{
    public int Id { get; set; }
    public required string Clave { get; set; }
    public string? Valor { get; set; }
    public string? Descripcion { get; set; }
}
