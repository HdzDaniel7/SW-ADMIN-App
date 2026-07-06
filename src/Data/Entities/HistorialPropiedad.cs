namespace SWDataExtractor.Data.Entities;

public class HistorialPropiedad
{
    public int Id { get; set; }
    public required string LoteId { get; set; }
    public int ArchivoId { get; set; }
    public string? Configuracion { get; set; }
    public required string Propiedad { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public required string Usuario { get; set; }
    public required string Fecha { get; set; }
    public required string Resultado { get; set; }

    public Archivo Archivo { get; set; } = null!;
}
