using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

public sealed class EscaneadorCarpetasTests : IDisposable
{
    private readonly TestDb _testDb;
    private readonly string _carpetaTemp;
    private readonly EscaneadorCarpetas _sut;

    public EscaneadorCarpetasTests()
    {
        _testDb     = new TestDb();
        _carpetaTemp = Path.Combine(Path.GetTempPath(), $"swtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_carpetaTemp);

        var cfg = new ConfiguracionExtraccion
        {
            CarpetasRaiz         = [_carpetaTemp],
            ExtensionesIncluidas = [".sldprt", ".sldasm"],
            PatronesExcluidos    = ["~$*"]
        };
        _sut = new EscaneadorCarpetas(
            _testDb.Db,
            Options.Create(cfg),
            NullLogger<EscaneadorCarpetas>.Instance);
    }

    [Fact]
    public async Task Escanear_CarpetaVacia_NingunArchivoProcesado()
    {
        var r = await _sut.EscanearAsync(default);

        Assert.Equal(0, r.Nuevos + r.Actualizados + r.SinCambios + r.Errores);
    }

    [Fact]
    public async Task Escanear_ArchivosNuevos_InsertaComoPendiente()
    {
        CrearArchivo("pieza1.sldprt");
        CrearArchivo("pieza2.sldprt");

        var r = await _sut.EscanearAsync(default);

        Assert.Equal(2, r.Nuevos);
        Assert.Equal(2, _testDb.Db.Archivos.Count());
        Assert.All(_testDb.Db.Archivos, a => Assert.Equal("pendiente", a.EstadoRapido));
    }

    [Fact]
    public async Task Escanear_ExtensionNoIncluida_IgnoraArchivo()
    {
        CrearArchivo("documento.pdf");
        CrearArchivo("pieza.sldprt");

        var r = await _sut.EscanearAsync(default);

        Assert.Equal(1, r.Nuevos);
        Assert.Equal("pieza.sldprt", _testDb.Db.Archivos.Single().Nombre);
    }

    [Fact]
    public async Task Escanear_SegundaPasadaSinCambios_ContadorSinCambios()
    {
        CrearArchivo("pieza.sldprt", "contenido_v1");
        await _sut.EscanearAsync(default);

        var r = await _sut.EscanearAsync(default);

        Assert.Equal(0, r.Nuevos + r.Actualizados);
        Assert.Equal(1, r.SinCambios);
    }

    [Fact]
    public async Task Escanear_ContenidoModificado_MarcaEstadoPendiente()
    {
        var ruta = CrearArchivo("pieza.sldprt", "v1");
        await _sut.EscanearAsync(default);

        var archivo = _testDb.Db.Archivos.Single();
        archivo.EstadoRapido = "ok";
        await _testDb.Db.SaveChangesAsync();

        File.WriteAllText(ruta, "v2_contenido_diferente_abc");

        await _sut.EscanearAsync(default);

        Assert.Equal("pendiente", _testDb.Db.Archivos.Single().EstadoRapido);
    }

    [Fact]
    public async Task Escanear_ArchivoEliminadoDeDisco_MarcaOmitido()
    {
        var ruta = CrearArchivo("pieza.sldprt");
        await _sut.EscanearAsync(default);
        File.Delete(ruta);

        var r = await _sut.EscanearAsync(default);

        Assert.Equal(1, r.Eliminados);
        Assert.Equal("omitido", _testDb.Db.Archivos.Single().EstadoRapido);
    }

    [Fact]
    public async Task Escanear_PatronExcluido_IgnoraArchivosTemporal()
    {
        CrearArchivo("~$temporal.sldprt");
        CrearArchivo("pieza_real.sldprt");

        var r = await _sut.EscanearAsync(default);

        Assert.Equal(1, r.Nuevos);
        Assert.Equal("pieza_real.sldprt", _testDb.Db.Archivos.Single().Nombre);
    }

    private string CrearArchivo(string nombre, string contenido = "datos de prueba")
    {
        var ruta = Path.Combine(_carpetaTemp, nombre);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    public void Dispose()
    {
        _testDb.Dispose();
        if (Directory.Exists(_carpetaTemp))
            Directory.Delete(_carpetaTemp, recursive: true);
    }
}
