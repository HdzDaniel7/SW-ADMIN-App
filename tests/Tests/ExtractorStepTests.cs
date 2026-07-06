using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Core.Contratos;

namespace SWDataExtractor.Tests;

// ExtractorStep no usa BD ni SolidWorks: los tests trabajan sobre archivos temporales.
public sealed class ExtractorStepTests : IDisposable
{
    private readonly ExtractorStep _sut = new();
    private readonly List<string> _archivosTemp = [];

    private const string HeaderCompleto = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('Pieza de prueba'),'2;1');
        FILE_NAME('soporte_motor.step','2026-05-14T09:30:00',('J. Perez'),('ACME S.A.'),
          'SwSTEP 2.0','SolidWorks 2023','');
        FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));
        ENDSEC;
        DATA;
        #1=CARTESIAN_POINT('',(0.,0.,0.));
        ENDSEC;
        END-ISO-10303-21;
        """;

    private string CrearArchivo(string contenido, string extension = ".step")
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"swde-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(ruta, contenido);
        _archivosTemp.Add(ruta);
        return ruta;
    }

    public void Dispose()
    {
        foreach (var ruta in _archivosTemp)
            try { File.Delete(ruta); } catch { /* limpieza best-effort */ }
    }

    [Theory]
    [InlineData("pieza.stp",  true)]
    [InlineData("pieza.STEP", true)]
    [InlineData("pieza.sldprt", false)]
    [InlineData("pieza.igs",  false)]
    public void PuedeProcesar_SoloExtensionesStep(string ruta, bool esperado) =>
        Assert.Equal(esperado, _sut.PuedeProcesar(ruta));

    [Fact]
    public async Task ExtraerAsync_HeaderCompleto_DevuelvePropiedadesYAutor()
    {
        var ruta = CrearArchivo(HeaderCompleto);

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        Assert.Equal("J. Perez", r.Archivo?.Autor);

        var props = r.Propiedades.ToDictionary(p => p.Nombre, p => p.Valor);
        Assert.Equal("soporte_motor.step",   props["STEP_Nombre"]);
        Assert.Equal("2026-05-14T09:30:00",  props["STEP_Fecha"]);
        Assert.Equal("ACME S.A.",            props["STEP_Organizacion"]);
        Assert.Equal("SolidWorks 2023",      props["STEP_SistemaOrigen"]);
        Assert.Equal("Pieza de prueba",      props["STEP_Descripcion"]);
        Assert.StartsWith("AP214",           props["STEP_Esquema"]);
        // Todas a nivel documento (sin configuración): un STEP no tiene configuraciones.
        Assert.All(r.Propiedades, p => Assert.Null(p.Configuracion));
    }

    [Fact]
    public async Task ExtraerAsync_ComillasEscapadas_ConservaElTexto()
    {
        var ruta = CrearArchivo("""
            ISO-10303-21;
            HEADER;
            FILE_NAME('pieza ''especial''.stp','2026-01-01',(''),($),'','','');
            ENDSEC;
            """);

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        Assert.Equal("pieza 'especial'.stp",
            r.Propiedades.Single(p => p.Nombre == "STEP_Nombre").Valor);
        // Autor vacío y organización $ (nulo) no generan propiedades ni autor.
        Assert.Null(r.Archivo?.Autor);
        Assert.DoesNotContain(r.Propiedades, p => p.Nombre is "STEP_Autor" or "STEP_Organizacion");
    }

    [Fact]
    public async Task ExtraerAsync_ComentariosEnElHeader_NoSeTomanComoArgumentos()
    {
        // Caso real (MRP300_V3.stp): cada campo de FILE_NAME anotado con /* comentario */.
        var ruta = CrearArchivo("""
            ISO-10303-21;
            HEADER;
            FILE_NAME(
            /* name */ 'export0',
            /* time_stamp */ '2023-05-02T10:00:00',
            /* author */ ('yo'),
            /* organization */ (''),
            /* preprocessor_version */ 'kernel',
            /* originating_system */ 'FreeCAD',
            /* authorisation */ '');
            ENDSEC;
            """);

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        var props = r.Propiedades.ToDictionary(p => p.Nombre, p => p.Valor);
        Assert.Equal("export0", props["STEP_Nombre"]);
        Assert.Equal("yo",      props["STEP_Autor"]);
        Assert.Equal("FreeCAD", props["STEP_SistemaOrigen"]);
        Assert.DoesNotContain(r.Propiedades, p => p.Valor!.Contains("/*") || p.Valor.Contains("*/"));
    }

    [Fact]
    public async Task ExtraerAsync_SinFileName_OkConAdvertencia()
    {
        var ruta = CrearArchivo("""
            ISO-10303-21;
            HEADER;
            FILE_SCHEMA(('CONFIG_CONTROL_DESIGN'));
            ENDSEC;
            """);

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        Assert.Contains(r.Advertencias, a => a.Contains("FILE_NAME"));
        Assert.StartsWith("AP203", r.Propiedades.Single(p => p.Nombre == "STEP_Esquema").Valor);
    }

    private const string EnsambleStep = """
        ISO-10303-21;
        HEADER;
        FILE_NAME('ensamble.step','2026-01-01',('A'),(''),'','','');
        ENDSEC;
        DATA;
        #1=PRODUCT('Ensamble','Ensamble','',(#90));
        #2=PRODUCT('SubEnsamble','SubEnsamble','',(#90));
        #3=PRODUCT('Tornillo M8','Tornillo M8','',(#90));
        #11=PRODUCT_DEFINITION_FORMATION('','',#1);
        #12=PRODUCT_DEFINITION_FORMATION('','',#2);
        #13=PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE('','',#3,.NOT_KNOWN.);
        #21=PRODUCT_DEFINITION('design','',#11,#91);
        #22=PRODUCT_DEFINITION('design','',#12,#91);
        #23=PRODUCT_DEFINITION('design','',#13,#91);
        #31=NEXT_ASSEMBLY_USAGE_OCCURRENCE('o1','','',#21,#22,$);
        #32=NEXT_ASSEMBLY_USAGE_OCCURRENCE('o2','','',#22,#23,$);
        #33=NEXT_ASSEMBLY_USAGE_OCCURRENCE('o3','','',#22,#23,$);
        ENDSEC;
        END-ISO-10303-21;
        """;

    [Fact]
    public async Task ExtraerAsync_EnsambleConNauo_GeneraArbolBomEnDatosExtra()
    {
        var ruta = CrearArchivo(EnsambleStep);

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        Assert.Equal("3", r.Propiedades.Single(p => p.Nombre == "STEP_Componentes").Valor);
        Assert.NotNull(r.DatosExtraJson);

        using var json = System.Text.Json.JsonDocument.Parse(r.DatosExtraJson!);
        var bom = json.RootElement.GetProperty("bom_step");
        Assert.Equal("Ensamble", bom.GetProperty("raiz").GetString());
        Assert.Equal(3, bom.GetProperty("total_ocurrencias").GetInt32());

        // Ensamble → SubEnsamble ×1 → Tornillo M8 ×2 (dos NAUO del mismo par = cantidad 2)
        var sub = bom.GetProperty("componentes").EnumerateArray().Single();
        Assert.Equal("SubEnsamble", sub.GetProperty("nombre").GetString());
        Assert.Equal(1, sub.GetProperty("cantidad").GetInt32());
        var tornillo = sub.GetProperty("hijos").EnumerateArray().Single();
        Assert.Equal("Tornillo M8", tornillo.GetProperty("nombre").GetString());
        Assert.Equal(2, tornillo.GetProperty("cantidad").GetInt32());
    }

    [Fact]
    public async Task ExtraerAsync_PiezaSinNauo_NoGeneraDatosExtra()
    {
        var ruta = CrearArchivo(HeaderCompleto); // tiene DATA con geometría pero sin NAUO

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        Assert.Null(r.DatosExtraJson);
        Assert.DoesNotContain(r.Propiedades, p => p.Nombre == "STEP_Componentes");
    }

    [Fact]
    public async Task ExtraerAsync_SinAlcanceEstructura_OmiteLaSeccionData()
    {
        var ruta = CrearArchivo(EnsambleStep);

        var r = await _sut.ExtraerAsync(
            new SolicitudExtraccion(ruta, AlcanceExtraccion.Propiedades), default);

        Assert.Equal(EstadoExtraccion.Ok, r.Estado);
        Assert.Null(r.DatosExtraJson);
    }

    [Fact]
    public async Task ExtraerAsync_ArchivoSinFirmaIso_DevuelveError()
    {
        var ruta = CrearArchivo("esto no es un STEP, es un archivo renombrado");

        var r = await _sut.ExtraerAsync(new SolicitudExtraccion(ruta, AlcanceExtraccion.Rapida), default);

        Assert.Equal(EstadoExtraccion.Error, r.Estado);
        Assert.Contains("ISO-10303-21", r.MensajeError);
    }

    [Fact]
    public async Task ExtraerAsync_ArchivoInexistente_DevuelveErrorNoExcepcion()
    {
        var r = await _sut.ExtraerAsync(
            new SolicitudExtraccion(Path.Combine(Path.GetTempPath(), "no-existe.step"), AlcanceExtraccion.Rapida),
            default);

        Assert.Equal(EstadoExtraccion.Error, r.Estado);
    }
}
