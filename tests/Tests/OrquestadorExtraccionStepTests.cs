using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.Tests.Helpers;

namespace SWDataExtractor.Tests;

// Integración orquestador → ExtractorStep → BD: un .step se extrae en modo Rápido
// sin SolidWorks ni licencia, y en modo Auto no queda reintentando Profunda por siempre.
public sealed class OrquestadorExtraccionStepTests : IDisposable
{
    private readonly TestDb _testDb = new();
    private readonly string _rutaStep;
    private readonly OrquestadorExtraccion _sut;

    public OrquestadorExtraccionStepTests()
    {
        _rutaStep = Path.Combine(Path.GetTempPath(), $"swde-orq-{Guid.NewGuid():N}.step");
        File.WriteAllText(_rutaStep, """
            ISO-10303-21;
            HEADER;
            FILE_NAME('brida.step','2026-03-01T08:00:00',('M. Diaz'),('Taller Norte'),
              'SwSTEP 2.0','SolidWorks 2024','');
            FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));
            ENDSEC;
            DATA;
            #1=PRODUCT('Brida','Brida','',(#90));
            #2=PRODUCT('Perno','Perno','',(#90));
            #11=PRODUCT_DEFINITION_FORMATION('','',#1);
            #12=PRODUCT_DEFINITION_FORMATION('','',#2);
            #21=PRODUCT_DEFINITION('design','',#11,#91);
            #22=PRODUCT_DEFINITION('design','',#12,#91);
            #31=NEXT_ASSEMBLY_USAGE_OCCURRENCE('o1','','',#21,#22,$);
            ENDSEC;
            END-ISO-10303-21;
            """);

        _sut = new OrquestadorExtraccion(
            _testDb.Db,
            [new ExtractorStep()],
            Options.Create(new ConfiguracionExtraccion()),
            NullLogger<OrquestadorExtraccion>.Instance);
    }

    public void Dispose()
    {
        _testDb.Dispose();
        try { File.Delete(_rutaStep); } catch { /* limpieza best-effort */ }
    }

    private async Task<Archivo> SembrarArchivoAsync()
    {
        var archivo = new Archivo
        {
            Ruta = _rutaStep, Nombre = Path.GetFileName(_rutaStep), Tipo = "step",
            EstadoRapido = "pendiente", EstadoProfundo = "pendiente", Origen = "sistema_archivos"
        };
        _testDb.Db.Archivos.Add(archivo);
        await _testDb.Db.SaveChangesAsync();
        return archivo;
    }

    [Fact]
    public async Task ProcesarUno_Rapido_ExtraeStepSinSolidWorks()
    {
        var archivo = await SembrarArchivoAsync();

        await _sut.ProcesarUnoAsync(archivo.Id, ModoExtraccion.Rapido, default);

        Assert.Equal("ok", archivo.EstadoRapido);
        Assert.Equal("M. Diaz", archivo.Autor);

        var props = await _testDb.Db.Propiedades
            .Where(p => p.ArchivoId == archivo.Id)
            .ToDictionaryAsync(p => p.Nombre, p => p.Valor);
        Assert.Equal("brida.step",      props["STEP_Nombre"]);
        Assert.Equal("SolidWorks 2024", props["STEP_SistemaOrigen"]);

        // La estructura interna (1 ocurrencia Brida→Perno) queda en datos_extra_json.
        Assert.Contains("bom_step", archivo.DatosExtraJson);
        Assert.Equal("1", props["STEP_Componentes"]);
    }

    [Fact]
    public async Task ProcesarPendientes_Auto_NoReintentaProfundaEnStep()
    {
        var archivo = await SembrarArchivoAsync();
        await _sut.ProcesarUnoAsync(archivo.Id, ModoExtraccion.Rapido, default);

        // Segundo pase en Auto: el archivo entra a la consulta (Profunda "pendiente"),
        // pero DeterminarAlcanceAuto debe saltarlo — un STEP no tiene features nativos.
        var resumen = await _sut.ProcesarPendientesAsync(ModoExtraccion.Auto, default);

        Assert.Equal(0, resumen.Errores);
        Assert.Equal("ok", archivo.EstadoRapido);
        Assert.Null(archivo.MensajeError);
    }
}
