using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

public sealed class ServicioBomTests : IDisposable
{
    private readonly TestDb _testDb;
    private readonly ServicioBom _sut;

    public ServicioBomTests()
    {
        _testDb = new TestDb();
        _sut    = new ServicioBom(_testDb.Db);
    }

    [Fact]
    public async Task ObtenerBomIndentado_SinConfiguracionActiva_RetornaVacio()
    {
        var asm = SeedArchivo("ensamble.sldasm", "ensamble");
        await _testDb.Db.SaveChangesAsync();

        var bom = await _sut.ObtenerBomIndentadoAsync(asm.Id);

        Assert.Empty(bom);
    }

    [Fact]
    public async Task ObtenerBomIndentado_EnsambleConDosComponentes_DevuelveRaizMasHijos()
    {
        var asm  = SeedArchivo("ensamble.sldasm", "ensamble");
        var pza1 = SeedArchivo("pieza1.sldprt",  "pieza");
        var pza2 = SeedArchivo("pieza2.sldprt",  "pieza");
        var cfg  = SeedConfig(asm, activa: true);
        SeedComponente(asm, cfg, pza1, 1);
        SeedComponente(asm, cfg, pza2, 2);
        await _testDb.Db.SaveChangesAsync();

        var bom = await _sut.ObtenerBomIndentadoAsync(asm.Id);

        // raíz en nivel 0 + 2 hijos en nivel 1
        Assert.Equal(3, bom.Count);
        Assert.Equal(0, bom[0].Nivel);
        Assert.All(bom.Skip(1), item => Assert.Equal(1, item.Nivel));
    }

    [Fact]
    public async Task ObtenerBomAplanado_EnsambleAnidado_AcumulaCantidadesTotales()
    {
        // asm1 contiene asm2 (x2); asm2 contiene pieza (x3) → total pieza = 6
        // El CTE actual usa el cfgActivaId del ensamble raíz en todos los niveles,
        // por lo que los componentes de asm2 también usan cfg1 para ser encontrados.
        var asm1  = SeedArchivo("asm1.sldasm", "ensamble");
        var asm2  = SeedArchivo("asm2.sldasm", "ensamble");
        var pieza = SeedArchivo("pieza.sldprt", "pieza");
        var cfg1  = SeedConfig(asm1, activa: true);
        SeedComponente(asm1, cfg1, asm2,  2);
        SeedComponente(asm2, cfg1, pieza, 3);
        await _testDb.Db.SaveChangesAsync();

        var flat = await _sut.ObtenerBomAplanadoAsync(asm1.Id);

        var itemPieza = flat.Single(i => i.Nombre == "pieza.sldprt");
        Assert.Equal(6, itemPieza.CantidadTotal);
    }

    [Fact]
    public async Task ObtenerWhereUsed_PiezaEnUnEnsamble_DevuelveEseEnsamble()
    {
        var asm   = SeedArchivo("asm.sldasm",   "ensamble");
        var pieza = SeedArchivo("pieza.sldprt", "pieza");
        var cfg   = SeedConfig(asm, activa: true);
        SeedComponente(asm, cfg, pieza, 1);
        await _testDb.Db.SaveChangesAsync();

        var wu = await _sut.ObtenerWhereUsedAsync(pieza.Id);

        Assert.Single(wu);
        Assert.Equal(asm.Id, wu[0].EnsambleArchivoId);
    }

    [Fact]
    public async Task CompararBom_MismoEnsamble_DiffVacia()
    {
        var asm   = SeedArchivo("asm.sldasm",   "ensamble");
        var pieza = SeedArchivo("pieza.sldprt", "pieza");
        var cfg   = SeedConfig(asm, activa: true);
        SeedComponente(asm, cfg, pieza, 2);
        await _testDb.Db.SaveChangesAsync();

        var diff = await _sut.CompararBomAsync(asm.Id, asm.Id);

        Assert.Empty(diff);
    }

    [Fact]
    public async Task CompararBom_ComponenteAgregadoEnSegundo_DetectaAgregado()
    {
        var asm1 = SeedArchivo("asm1.sldasm",   "ensamble");
        var asm2 = SeedArchivo("asm2.sldasm",   "ensamble");
        var pza1 = SeedArchivo("pieza1.sldprt", "pieza");
        var pza2 = SeedArchivo("pieza2.sldprt", "pieza");
        var cfg1 = SeedConfig(asm1, activa: true);
        var cfg2 = SeedConfig(asm2, activa: true);
        SeedComponente(asm1, cfg1, pza1, 1);
        SeedComponente(asm2, cfg2, pza1, 1);
        SeedComponente(asm2, cfg2, pza2, 1);
        await _testDb.Db.SaveChangesAsync();

        var diff = await _sut.CompararBomAsync(asm1.Id, asm2.Id);

        Assert.Single(diff);
        Assert.Equal("agregado",        diff[0].Cambio);
        Assert.Equal("pieza2.sldprt",   diff[0].Nombre);
    }

    // ── helpers de seed ─────────────────────────────────────────────────────────

    private Archivo SeedArchivo(string nombre, string tipo)
    {
        var a = new Archivo
        {
            Ruta           = @"C:\sw\" + nombre,
            Nombre         = nombre,
            Tipo           = tipo,
            EstadoRapido   = "ok",
            EstadoProfundo = "pendiente",
            Origen         = "test"
        };
        _testDb.Db.Archivos.Add(a);
        return a;
    }

    private Configuracion SeedConfig(Archivo archivo, bool activa)
    {
        var c = new Configuracion { Archivo = archivo, Nombre = "Default", EsActiva = activa };
        _testDb.Db.Configuraciones.Add(c);
        return c;
    }

    private void SeedComponente(Archivo ensamble, Configuracion cfg, Archivo hijo, int cantidad)
    {
        _testDb.Db.Componentes.Add(new Componente
        {
            EnsambleArchivo   = ensamble,
            EnsambleConfig    = cfg,
            ComponenteArchivo = hijo,
            RutaReferenciada  = hijo.Ruta,
            Cantidad          = cantidad
        });
    }

    public void Dispose() => _testDb.Dispose();
}
