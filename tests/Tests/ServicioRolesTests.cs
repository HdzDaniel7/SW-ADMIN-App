using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

public sealed class ServicioRolesTests : IDisposable
{
    private readonly TestDb _testDb;
    private readonly ServicioRoles _sut;

    public ServicioRolesTests()
    {
        _testDb = new TestDb();
        _sut    = new ServicioRoles(_testDb.Db);
    }

    public void Dispose() => _testDb.Dispose();

    [Fact]
    public async Task ObtenerRol_SinAjusteGuardado_DevuelveAdministrador()
    {
        var rol = await _sut.ObtenerRolAsync();

        Assert.Equal(Rol.Administrador, rol);
    }

    [Fact]
    public async Task CambiarRol_NuevoRol_SePersisteYSeRecupera()
    {
        await _sut.CambiarRolAsync(Rol.Visualizador);

        var rol = await _sut.ObtenerRolAsync();

        Assert.Equal(Rol.Visualizador, rol);
    }

    [Fact]
    public async Task CambiarRol_DosVeces_UltimoValorPersiste()
    {
        await _sut.CambiarRolAsync(Rol.Operador);
        await _sut.CambiarRolAsync(Rol.Visualizador);

        var rol = await _sut.ObtenerRolAsync();

        Assert.Equal(Rol.Visualizador, rol);
        // Solo debe existir un registro en ajustes_app para la clave RolActual
        Assert.Single(_testDb.Db.AjustesApp.Where(a => a.Clave == "RolActual"));
    }

    [Theory]
    [InlineData(Rol.Visualizador, false, false, false, true,  true)]
    [InlineData(Rol.Operador,     true,  true,  true,  true,  true)]
    [InlineData(Rol.Administrador,true,  true,  true,  true,  true)]
    public void FuncionalidadesParaRol_DevuelvePermisosCorrectos(
        Rol rol,
        bool extProfunda, bool escritura, bool etiquetas, bool bom, bool excel)
    {
        var cfg = ServicioRoles.FuncionalidadesParaRol(rol);

        Assert.Equal(extProfunda, cfg.ExtraccionProfunda);
        Assert.Equal(escritura,   cfg.EscrituraPropiedades);
        Assert.Equal(etiquetas,   cfg.Etiquetas);
        Assert.Equal(bom,         cfg.ComparacionBom);
        Assert.Equal(excel,       cfg.ExportacionExcel);
    }
}
