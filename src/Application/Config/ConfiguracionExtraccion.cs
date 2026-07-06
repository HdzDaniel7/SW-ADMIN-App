namespace SWDataExtractor.Application.Config;

public class ConfiguracionExtraccion
{
    public List<string> CarpetasRaiz { get; set; } = [];
    // Alineado con appsettings.json: .stp/.step los atiende ExtractorStep sin SW ni licencia.
    public List<string> ExtensionesIncluidas { get; set; } = [".sldprt", ".sldasm", ".stp", ".step"];
    public List<string> PatronesExcluidos { get; set; } = ["~$*", "*\\backup\\*"];
    public int TimeoutPorArchivoSegundos { get; set; } = 300;
    public int ReiniciarSwCadaNArchivos { get; set; } = 50;
    public int MaxReintentos { get; set; } = 2;
    public string CarpetaCachePreviews { get; set; } = "%LOCALAPPDATA%/SWDataExtractor/previews";
}
