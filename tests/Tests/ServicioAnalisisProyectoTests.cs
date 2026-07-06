using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

public sealed class ServicioAnalisisProyectoTests : IDisposable
{
    private readonly TestDb _testDb;
    private readonly ServicioAnalisisProyecto _sut;

    public ServicioAnalisisProyectoTests()
    {
        _testDb = new TestDb();
        var escaneador = new EscaneadorCarpetas(
            _testDb.Db,
            Options.Create(new ConfiguracionExtraccion()),
            NullLogger<EscaneadorCarpetas>.Instance);
        _sut = new ServicioAnalisisProyecto(_testDb.Db, escaneador);
    }

    // ── Duplicados ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Duplicados_MismoHashDistintaRuta_AgrupaJuntos()
    {
        SeedArchivo(@"C:\p1\base.sldprt",  hash: "AAA");
        SeedArchivo(@"C:\p2\copia.sldprt", hash: "AAA");
        SeedArchivo(@"C:\p1\otra.sldprt",  hash: "BBB");
        await _testDb.Db.SaveChangesAsync();

        var grupos = await _sut.ObtenerDuplicadosAsync();

        Assert.Single(grupos);
        Assert.Equal(2, grupos[0].Archivos.Count);
    }

    [Fact]
    public async Task Duplicados_ArchivoOmitido_SeExcluye()
    {
        SeedArchivo(@"C:\p1\a.sldprt", hash: "AAA");
        SeedArchivo(@"C:\p2\b.sldprt", hash: "AAA", estado: "omitido");
        await _testDb.Db.SaveChangesAsync();

        Assert.Empty(await _sut.ObtenerDuplicadosAsync());
    }

    // ── Referencias rotas ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReferenciasRotas_ComponenteSinArchivo_SeReporta_ToolboxNo()
    {
        var asm = SeedArchivo(@"C:\p\asm.sldasm", tipo: "ensamble");
        var cfg = new Configuracion { Archivo = asm, Nombre = "Default", EsActiva = true };
        _testDb.Db.Configuraciones.Add(cfg);
        _testDb.Db.Componentes.Add(new Componente
        {
            EnsambleArchivo = asm, EnsambleConfig = cfg,
            RutaReferenciada = @"C:\perdida\pieza.sldprt", Cantidad = 2
        });
        _testDb.Db.Componentes.Add(new Componente
        {
            EnsambleArchivo = asm, EnsambleConfig = cfg,
            RutaReferenciada = @"C:\SOLIDWORKS\toolbox\tornillo.sldprt", Cantidad = 8, EsToolbox = true
        });
        // Toolbox NO marcado (extracción SwApi aún no detecta es_toolbox) → heurística por ruta
        _testDb.Db.Componentes.Add(new Componente
        {
            EnsambleArchivo = asm, EnsambleConfig = cfg,
            RutaReferenciada = @"C:\SOLIDWORKS Data\browser\Ansi Metric\bolts\oval_head.sldprt", Cantidad = 10
        });
        await _testDb.Db.SaveChangesAsync();

        var rotas = await _sut.ObtenerReferenciasRotasAsync();

        Assert.Single(rotas);
        Assert.Equal(@"C:\perdida\pieza.sldprt", rotas[0].RutaReferenciada);
        Assert.Equal(2, rotas[0].Cantidad);
    }

    // ── Posibles versiones ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Soporte_v2.SLDPRT",        "soporte")]
    [InlineData("Soporte-v10.sldprt",       "soporte")]
    [InlineData("Soporte (3).sldprt",       "soporte")]
    [InlineData("Soporte_final.sldprt",     "soporte")]
    [InlineData("Soporte_v2_final.sldprt",  "soporte")]
    [InlineData("Soporte_2026-01-15.sldprt","soporte")]
    [InlineData("Soporte.sldprt",           "soporte")]
    public void NormalizarNombre_QuitaSufijosDeVersion(string nombre, string esperado) =>
        Assert.Equal(esperado, ServicioAnalisisProyecto.NormalizarNombre(nombre));

    [Fact]
    public async Task Versiones_MismoNombreBaseDistintoHash_Agrupa_MasRecientePrimero()
    {
        SeedArchivo(@"C:\p\soporte_v1.sldprt", hash: "A", fechaIso: "2026-01-01T10:00:00Z");
        SeedArchivo(@"C:\p\soporte_v2.sldprt", hash: "B", fechaIso: "2026-06-01T10:00:00Z");
        SeedArchivo(@"C:\p\eje.sldprt",        hash: "C");
        await _testDb.Db.SaveChangesAsync();

        var grupos = await _sut.ObtenerPosiblesVersionesAsync();

        Assert.Single(grupos);
        Assert.Equal("soporte", grupos[0].NombreBase);
        Assert.Equal("soporte_v2.sldprt", grupos[0].Versiones[0].Nombre); // más reciente primero
    }

    [Fact]
    public async Task Versiones_MismoHash_NoSeReporta_EsDuplicadoNoVersion()
    {
        SeedArchivo(@"C:\p\soporte_v1.sldprt", hash: "A");
        SeedArchivo(@"C:\p\soporte_v2.sldprt", hash: "A");
        await _testDb.Db.SaveChangesAsync();

        Assert.Empty(await _sut.ObtenerPosiblesVersionesAsync());
    }

    // ── Árbol de carpetas ─────────────────────────────────────────────────────

    [Fact]
    public async Task Arbol_ArchivosEnSubcarpetas_ConteosAcumulados()
    {
        SeedArchivo(@"C:\proy\a.sldprt");
        SeedArchivo(@"C:\proy\sub\b.sldprt");
        SeedArchivo(@"C:\proy\sub\c.sldprt");
        await _testDb.Db.SaveChangesAsync();

        var arbol = await _sut.ObtenerArbolCarpetasAsync();

        var raiz = Assert.Single(arbol);
        Assert.Equal(3, raiz.TotalArchivos);
        var sub = Assert.Single(raiz.Hijas);
        Assert.Equal("sub", sub.Nombre);
        Assert.Equal(2, sub.TotalArchivos);
    }

    [Fact]
    public async Task ArchivosDeCarpeta_SoloDirectos_SinSubcarpetas()
    {
        SeedArchivo(@"C:\proy\a.sldprt");
        SeedArchivo(@"C:\proy\sub\b.sldprt");
        await _testDb.Db.SaveChangesAsync();

        var archivos = await _sut.ObtenerArchivosDeCarpetaAsync(@"C:\proy");

        Assert.Single(archivos);
        Assert.Equal("a.sldprt", archivos[0].Nombre);
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsamblesTopLevel_ExcluyeLosQueSonComponenteDeOtro()
    {
        var padre = SeedArchivo(@"C:\p\padre.sldasm", tipo: "ensamble");
        var hijo  = SeedArchivo(@"C:\p\hijo.sldasm",  tipo: "ensamble");
        var cfg   = new Configuracion { Archivo = padre, Nombre = "Default", EsActiva = true };
        _testDb.Db.Configuraciones.Add(cfg);
        _testDb.Db.Componentes.Add(new Componente
        {
            EnsambleArchivo = padre, EnsambleConfig = cfg,
            ComponenteArchivo = hijo, RutaReferenciada = hijo.Ruta, Cantidad = 1
        });
        await _testDb.Db.SaveChangesAsync();

        var topLevel = await _sut.ObtenerEnsamblesTopLevelAsync();

        Assert.Single(topLevel);
        Assert.Equal("padre.sldasm", topLevel[0].Nombre);
    }

    [Fact]
    public async Task CalcularSalud_ClasificaEstadosRotasYToolbox()
    {
        var pOk   = SeedArchivo(@"C:\p\ok.sldprt",   estado: "ok");
        var pPend = SeedArchivo(@"C:\p\pend.sldprt", estado: "pendiente");
        await _testDb.Db.SaveChangesAsync();

        var bom = new List<ItemBom>
        {
            new(0, 99,      "asm",  @"C:\p\asm.sldasm", "ensamble", 1, false, false, false),
            new(1, pOk.Id,  "ok",   pOk.Ruta,   "pieza", 1, false, false, false),
            new(1, pPend.Id,"pend", pPend.Ruta, "pieza", 2, false, false, false),
            new(1, null,    "rota", @"C:\x\perdida.sldprt", "otro", 1, false, false, false),
            new(1, null,    "tornillo", @"C:\SW\toolbox\t.sldprt", "otro", 4, true, false, false),
        };

        var salud = await _sut.CalcularSaludAsync(bom);

        Assert.Equal(4, salud.TotalComponentes);
        Assert.Equal(1, salud.Ok);
        Assert.Equal(1, salud.Pendientes);
        Assert.Equal(1, salud.ReferenciasRotas);
        Assert.Equal(1, salud.Toolbox);
    }

    // ── Cumplimiento de propiedades ───────────────────────────────────────────

    [Fact]
    public async Task Incumplimientos_ArchivoSinPropiedadObligatoria_SeReporta()
    {
        _testDb.Db.DiccionarioPropiedades.Add(new DiccionarioPropiedad
        {
            Nombre = "NumeroParte", Tipo = "texto", Obligatoria = true, Activa = true
        });
        var conProp = SeedArchivo(@"C:\p\ok.sldprt");
        var sinProp = SeedArchivo(@"C:\p\incompleta.sldprt");
        var pendiente = SeedArchivo(@"C:\p\pendiente.sldprt", estado: "pendiente");
        _testDb.Db.Propiedades.Add(new Propiedad
        {
            Archivo = conProp, Nombre = "NumeroParte", Valor = "P-001", ValorResuelto = "P-001"
        });
        await _testDb.Db.SaveChangesAsync();

        var faltantes = await _sut.ObtenerIncumplimientosAsync();

        var falta = Assert.Single(faltantes); // solo la extraída OK sin la propiedad
        Assert.Equal("incompleta.sldprt", falta.Nombre);
        Assert.Equal("NumeroParte", falta.PropiedadFaltante);
    }

    [Fact]
    public async Task Incumplimientos_SinPropiedadesObligatorias_Vacio()
    {
        SeedArchivo(@"C:\p\a.sldprt");
        await _testDb.Db.SaveChangesAsync();

        Assert.Empty(await _sut.ObtenerIncumplimientosAsync());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Archivo SeedArchivo(
        string ruta, string tipo = "pieza", string? hash = null,
        string estado = "ok", string? fechaIso = null)
    {
        var a = new Archivo
        {
            Ruta           = ruta,
            Nombre         = Path.GetFileName(ruta),
            Tipo           = tipo,
            HashSha256     = hash,
            FechaModDisco  = fechaIso,
            EstadoRapido   = estado,
            EstadoProfundo = "pendiente",
            Origen         = "test"
        };
        _testDb.Db.Archivos.Add(a);
        return a;
    }

    public void Dispose() => _testDb.Dispose();
}
