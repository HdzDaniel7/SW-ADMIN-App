using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

public sealed class ServicioPropiedadesTests : IDisposable
{
    private readonly TestDb _testDb;
    private readonly ServicioPropiedades _sut;

    public ServicioPropiedadesTests()
    {
        _testDb = new TestDb();
        _sut    = new ServicioPropiedades(_testDb.Db, new EscritorStub());
    }

    [Fact]
    public async Task LeerPropiedades_SinDiccionario_DevuelvePropiedades()
    {
        var archivo = SeedArchivo();
        SeedPropiedad(archivo, "Material", "Acero");
        await _testDb.Db.SaveChangesAsync();

        var props = await _sut.LeerPropiedadesAsync(archivo.Id);

        Assert.Single(props);
        Assert.Equal("Material", props[0].Nombre);
        Assert.False(props[0].EsEstandar);
    }

    [Fact]
    public async Task LeerPropiedades_ConDiccionario_MarcaPropiedadComoEstandar()
    {
        var archivo = SeedArchivo();
        SeedPropiedad(archivo, "Material", "Acero");
        _testDb.Db.DiccionarioPropiedades.Add(new DiccionarioPropiedad
        {
            Nombre = "Material", Tipo = "texto", Activa = true, Obligatoria = false
        });
        await _testDb.Db.SaveChangesAsync();

        var props = await _sut.LeerPropiedadesAsync(archivo.Id);

        Assert.True(props.Single().EsEstandar);
    }

    [Fact]
    public async Task ValidarPropiedades_ObligatoriaAusente_RetornaError()
    {
        var archivo = SeedArchivo();
        _testDb.Db.DiccionarioPropiedades.Add(new DiccionarioPropiedad
        {
            Nombre = "Descripcion", Tipo = "texto", Activa = true, Obligatoria = true
        });
        await _testDb.Db.SaveChangesAsync();

        var r = await _sut.ValidarPropiedadesAsync(archivo.Id);

        Assert.Contains(r.Errores, e => e.Nombre == "Descripcion");
    }

    [Fact]
    public async Task ValidarPropiedades_ValorFueraDeListaPermitida_RetornaError()
    {
        var archivo = SeedArchivo();
        SeedPropiedad(archivo, "Estado", "INVALIDO", valorResuelto: "INVALIDO");
        _testDb.Db.DiccionarioPropiedades.Add(new DiccionarioPropiedad
        {
            Nombre                = "Estado",
            Tipo                  = "lista",
            Activa                = true,
            Obligatoria           = false,
            ValoresPermitidosJson = """["Borrador","Aprobado","Obsoleto"]"""
        });
        await _testDb.Db.SaveChangesAsync();

        var r = await _sut.ValidarPropiedadesAsync(archivo.Id);

        Assert.Contains(r.Errores, e => e.Nombre == "Estado");
    }

    [Fact]
    public async Task PrepararLote_ValorIgualAlActual_OmiteConAdvertencia()
    {
        const string ruta = @"C:\piezas\pieza.sldprt";
        var archivo = SeedArchivo(ruta);
        SeedPropiedad(archivo, "Material", "Acero");
        await _testDb.Db.SaveChangesAsync();

        var preview = await _sut.PrepararLoteAsync(
            [new CambioPropiedad(ruta, null, "Material", "Acero")]);

        Assert.Empty(preview.Cambios);
        Assert.Single(preview.Advertencias);
    }

    [Fact]
    public async Task PrepararLote_ArchivoNoEncontradoEnBd_RetornaAdvertencia()
    {
        await _testDb.Db.SaveChangesAsync();

        var preview = await _sut.PrepararLoteAsync(
            [new CambioPropiedad(@"C:\no\existe.sldprt", null, "Material", "Aluminio")]);

        Assert.Empty(preview.Cambios);
        Assert.Contains(preview.Advertencias, a => a.Contains("no encontrado"));
    }

    [Fact]
    public async Task EscribirLote_EjecucionOk_PersisiteHistorialEnBd()
    {
        const string ruta = @"C:\piezas\pieza_hist.sldprt";
        var archivo = SeedArchivo(ruta);
        await _testDb.Db.SaveChangesAsync();

        var escritorSim = new EscritorSimulador();
        var sut = new ServicioPropiedades(_testDb.Db, escritorSim);

        var lote = new LoteEscritura("usuario_test",
            [new CambioPropiedad(ruta, null, "Descripcion", "Pieza nueva")]);

        await sut.EscribirLoteAsync(lote);

        var historial = _testDb.Db.HistorialPropiedades.ToList();
        Assert.Single(historial);
        Assert.Equal("usuario_test", historial[0].Usuario);
        Assert.Equal("Descripcion",  historial[0].Propiedad);
        Assert.Equal("ok",           historial[0].Resultado);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private Archivo SeedArchivo(string ruta = @"C:\test\pieza.sldprt")
    {
        var a = new Archivo
        {
            Ruta           = ruta,
            Nombre         = Path.GetFileName(ruta),
            Tipo           = "pieza",
            EstadoRapido   = "ok",
            EstadoProfundo = "pendiente",
            Origen         = "test"
        };
        _testDb.Db.Archivos.Add(a);
        return a;
    }

    private void SeedPropiedad(Archivo archivo, string nombre, string? valor, string? valorResuelto = null)
    {
        _testDb.Db.Propiedades.Add(new Propiedad
        {
            Archivo       = archivo,
            Nombre        = nombre,
            Valor         = valor,
            ValorResuelto = valorResuelto ?? valor,
            Tipo          = "texto"
        });
    }

    public void Dispose() => _testDb.Dispose();

    // Stub mínimo para no depender de DocManager en los tests
    private sealed class EscritorStub : IEscritorPropiedades
    {
        public Task<ResultadoEscrituraLote> EscribirAsync(LoteEscritura lote, CancellationToken ct) =>
            Task.FromResult(new ResultadoEscrituraLote(Guid.NewGuid().ToString(), []));
    }

    // Simulador que devuelve resultados OK por cada cambio solicitado
    private sealed class EscritorSimulador : IEscritorPropiedades
    {
        public Task<ResultadoEscrituraLote> EscribirAsync(LoteEscritura lote, CancellationToken ct)
        {
            var resultados = lote.Cambios
                .Select(c => new ResultadoCambio(c, null, "ok", null))
                .ToList();
            return Task.FromResult(new ResultadoEscrituraLote(Guid.NewGuid().ToString(), resultados));
        }
    }
}
