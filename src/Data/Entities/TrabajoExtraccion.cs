namespace SWDataExtractor.Data.Entities;

public class TrabajoExtraccion
{
    public int Id { get; set; }
    public int ArchivoId { get; set; }
    public required string Tipo { get; set; }
    public required string Estado { get; set; }
    public int Intentos { get; set; }
    public string? FechaEncolado { get; set; }
    public string? FechaInicio { get; set; }
    public string? FechaFin { get; set; }
    public long? DuracionMs { get; set; }
    public string? Mensaje { get; set; }

    public Archivo Archivo { get; set; } = null!;
}
