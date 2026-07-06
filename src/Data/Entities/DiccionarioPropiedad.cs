namespace SWDataExtractor.Data.Entities;

public class DiccionarioPropiedad
{
    public int Id { get; set; }
    public required string Nombre { get; set; }
    public required string Tipo { get; set; }
    public string? ValoresPermitidosJson { get; set; }
    public bool Obligatoria { get; set; }
    public string? Nivel { get; set; }
    public string? Descripcion { get; set; }
    public bool Activa { get; set; }
}
