namespace SWDataExtractor.Data.Entities;

public class Archivo
{
    public int Id { get; set; }
    public required string Ruta { get; set; }
    public required string Nombre { get; set; }
    public required string Tipo { get; set; }
    public string? HashSha256 { get; set; }
    public long? TamanoBytes { get; set; }
    public string? FechaModDisco { get; set; }
    public int? VersionSw { get; set; }
    public string? Autor { get; set; }
    public string? RutaPreview { get; set; }
    public string? FechaExtrRapida { get; set; }
    public string? FechaExtrProfunda { get; set; }
    public required string EstadoRapido { get; set; } = "pendiente";
    public required string EstadoProfundo { get; set; } = "pendiente";
    public string? MensajeError { get; set; }
    public required string Origen { get; set; } = "sistema_archivos";
    public string? DatosExtraJson { get; set; }

    public ICollection<Configuracion> Configuraciones { get; set; } = [];
    public ICollection<Propiedad> Propiedades { get; set; } = [];
    public ICollection<PropiedadFisica> PropiedadesFisicas { get; set; } = [];
    public ICollection<Feature> Features { get; set; } = [];
    public ICollection<HistorialPropiedad> HistorialPropiedades { get; set; } = [];
    public ICollection<TrabajoExtraccion> TrabajosExtraccion { get; set; } = [];
    public ICollection<ArchivoEtiqueta> ArchivoEtiquetas { get; set; } = [];
    public ICollection<Componente> ComponentesComoEnsamble { get; set; } = [];
    public ICollection<Componente> ComponentesComoComponente { get; set; } = [];
}
