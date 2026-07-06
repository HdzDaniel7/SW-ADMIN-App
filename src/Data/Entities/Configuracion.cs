namespace SWDataExtractor.Data.Entities;

public class Configuracion
{
    public int Id { get; set; }
    public int ArchivoId { get; set; }
    public required string Nombre { get; set; }
    public bool EsActiva { get; set; }
    public bool EsDerivada { get; set; }

    public Archivo Archivo { get; set; } = null!;
    public ICollection<Propiedad> Propiedades { get; set; } = [];
    public ICollection<PropiedadFisica> PropiedadesFisicas { get; set; } = [];
    public ICollection<Componente> ComponentesComoEnsamble { get; set; } = [];
}
