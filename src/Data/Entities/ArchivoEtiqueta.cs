namespace SWDataExtractor.Data.Entities;

public class ArchivoEtiqueta
{
    public int ArchivoId { get; set; }
    public int EtiquetaId { get; set; }

    public Archivo Archivo { get; set; } = null!;
    public Etiqueta Etiqueta { get; set; } = null!;
}
