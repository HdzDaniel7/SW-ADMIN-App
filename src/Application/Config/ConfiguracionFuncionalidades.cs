namespace SWDataExtractor.Application.Config;

public class ConfiguracionFuncionalidades
{
    public bool ExtraccionProfunda   { get; set; } = true;
    public bool EscrituraPropiedades { get; set; } = true;
    public bool Etiquetas            { get; set; } = true;
    public bool ComparacionBom       { get; set; } = true;
    public bool ExportacionExcel     { get; set; } = true;
}
