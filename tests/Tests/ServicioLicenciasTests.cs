using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

// ServicioLicencias usa DPAPI (solo Windows); estos tests solo corren en Windows,
// igual que el resto de la solución (WPF + COM SolidWorks).
[SupportedOSPlatform("windows")]
public sealed class ServicioLicenciasTests : IDisposable
{
    private readonly TestDb _testDb;
    private readonly ServicioLicencias _sut;

    public ServicioLicenciasTests()
    {
        _testDb = new TestDb();
        _sut    = new ServicioLicencias(_testDb.Db, NullLogger<ServicioLicencias>.Instance);
    }

    [Fact]
    public async Task GuardarYObtener_RoundTrip_DevuelveLaMismaClave()
    {
        await _sut.GuardarClaveDocManagerAsync("CLAVE-DE-PRUEBA-123");

        var clave = await _sut.ObtenerClaveDocManagerAsync();

        Assert.Equal("CLAVE-DE-PRUEBA-123", clave);
    }

    [Fact]
    public async Task Guardar_CifraElValorEnLaBd_NoQuedaEnTextoPlano()
    {
        await _sut.GuardarClaveDocManagerAsync("CLAVE-SECRETA");

        var ajuste = await _testDb.Db.AjustesApp
            .FirstAsync(a => a.Clave == ServicioLicencias.ClaveDocManager);

        Assert.DoesNotContain("CLAVE-SECRETA", ajuste.Valor);
    }

    [Fact]
    public async Task Obtener_ValorLegadoEnTextoPlano_SeDevuelveTalCual()
    {
        // Simula una clave guardada antes de introducir el cifrado DPAPI.
        _testDb.Db.AjustesApp.Add(new AjusteApp
        {
            Clave = ServicioLicencias.ClaveDocManager,
            Valor = "CLAVE-VIEJA-SIN-CIFRAR"
        });
        await _testDb.Db.SaveChangesAsync();

        var clave = await _sut.ObtenerClaveDocManagerAsync();

        Assert.Equal("CLAVE-VIEJA-SIN-CIFRAR", clave);
    }

    [Fact]
    public async Task Obtener_SinClaveGuardada_DevuelveNull()
    {
        var clave = await _sut.ObtenerClaveDocManagerAsync();

        Assert.Null(clave);
    }

    [Fact]
    public async Task GuardarNull_QuitaLaClave()
    {
        await _sut.GuardarClaveDocManagerAsync("ALGO");
        await _sut.GuardarClaveDocManagerAsync(null);

        var clave = await _sut.ObtenerClaveDocManagerAsync();

        Assert.Null(clave);
    }

    public void Dispose() => _testDb.Dispose();
}
