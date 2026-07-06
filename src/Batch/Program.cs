using Serilog;
using Serilog.Events;
using SWDataExtractor.Application.Config;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Batch;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data;
using SWDataExtractor.DocManager;
using SWDataExtractor.SwApi;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/swdataextractor-.log", rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((_, lc) => lc
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/swdataextractor-.log", rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

    builder.Services.Configure<ConfiguracionExtraccion>(
        builder.Configuration.GetSection("Extraccion"));
    var cadena = builder.Configuration["BaseDatos:CadenaConexion"] ?? "Data Source=swdata.db";
    builder.Services.AddAppDbContext(cadena);

    // DocManager primero: preferido para alcance Rápida (no requiere SW abierto). SwApi
    // segundo: único capaz de Profunda, y sustituto liviano de DocManager para Rápida
    // cuando no hay licencia configurada (ver OrquestadorExtraccion — selección por superset
    // de capacidades + reintento con el siguiente candidato si el primero falla).
    // La clave se lee de la BD (configurable desde la UI → Configuración, sin terminal);
    // si no hay ninguna guardada, cae a IConfiguration (user-secrets/appsettings).
    builder.Services.AddSingleton<IExtractorCad>(sp =>
        new ExtractorDocManager(ObtenerClaveDocManager(sp)));
    // StepHeader antes que SwApi: para .stp/.step lee el encabezado ISO-10303-21
    // sin SW ni licencia; SwApi queda de respaldo (y único para alcance Profunda).
    builder.Services.AddSingleton<IExtractorCad, ExtractorStep>();
    builder.Services.AddSingleton<IExtractorCad, ExtractorSwApi>();

    builder.Services.AddScoped<EscaneadorCarpetas>();
    builder.Services.AddScoped<OrquestadorExtraccion>();
    builder.Services.AddScoped<ServicioBom>();
    builder.Services.AddScoped<ServicioPropiedades>();
    builder.Services.AddScoped<ServicioLicencias>();
    builder.Services.AddScoped<IEscritorPropiedades>(sp =>
        new EscritorDocManager(ObtenerClaveDocManager(sp)));

    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DbContextExtensions.InicializarAsync(db, cadena);
    }

    await host.RunAsync();
}
catch (Exception ex) { Log.Fatal(ex, "Fallo crítico al iniciar SWDataExtractor Batch"); }
finally { await Log.CloseAndFlushAsync(); }

// BD primero (se resuelve tras InicializarAsync, cuando Worker construye OrquestadorExtraccion
// por primera vez); IConfiguration de fallback si no hay clave guardada en ajustes_app.
static string ObtenerClaveDocManager(IServiceProvider sp)
{
    try
    {
        using var scope = sp.CreateScope();
        var licencias = scope.ServiceProvider.GetRequiredService<ServicioLicencias>();
        var claveBd = licencias.ObtenerClaveDocManagerAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(claveBd)) return claveBd;
    }
    catch { /* BD no disponible todavía; usar el fallback de configuración */ }
    return sp.GetRequiredService<IConfiguration>()["DocManager:LicenciaKey"] ?? "";
}
