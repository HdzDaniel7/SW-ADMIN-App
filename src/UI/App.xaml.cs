using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SWDataExtractor.Core.Contratos;
using SWDataExtractor.Data;
using SWDataExtractor.DocManager;
using SWDataExtractor.SwApi;
using SWDataExtractor.UI.ViewModels;
using SWDataExtractor.UI.Views;

// Aliases para evitar colisión de 'Application' con el namespace SWDataExtractor.Application
using AppConfig  = SWDataExtractor.Application.Config.ConfiguracionExtraccion;
using AppEscan   = SWDataExtractor.Application.Servicios.EscaneadorCarpetas;
using AppOrq     = SWDataExtractor.Application.Servicios.OrquestadorExtraccion;
using AppBom     = SWDataExtractor.Application.Servicios.ServicioBom;
using AppProps   = SWDataExtractor.Application.Servicios.ServicioPropiedades;
using AppTarea   = SWDataExtractor.Application.Servicios.ServicioTareaProgramada;
using AppRoles   = SWDataExtractor.Application.Servicios.ServicioRoles;
using AppLicencias = SWDataExtractor.Application.Servicios.ServicioLicencias;

namespace SWDataExtractor.UI;

public partial class App : System.Windows.Application
{
    private IHost _host = null!;
    private Mutex? _instanciaUnica;

    // Carpeta de logs fuera del directorio del exe: escribible aunque la app viva en una
    // ubicación de solo lectura, y sobrevive a actualizaciones del programa.
    private static readonly string CarpetaLogs = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SWDataExtractor", "logs");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instancia única: dos copias contra el mismo swdata.db (SQLite) pueden pisarse
        // escribiendo. El mutex vive en el campo para no ser recolectado.
        _instanciaUnica = new Mutex(true, @"Local\SWDataExtractor_UI", out bool esPrimera);
        if (!esPrimera)
        {
            MessageBox.Show("SWDataExtractor ya está en ejecución.", "SWDataExtractor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Log a archivo (rotación diaria): sin esto, un fallo en la máquina de un usuario
        // no deja ningún rastro diagnosticable.
        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(Path.Combine(CarpetaLogs, "ui-.log"), rollingInterval: Serilog.RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        RegistrarManejoGlobalDeExcepciones();
        Serilog.Log.Information("SWDataExtractor UI v{Version} iniciando",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration(cfg => cfg
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddUserSecrets<App>(optional: true))
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<AppConfig>(
                    ctx.Configuration.GetSection("Extraccion"));
                var cadena = ctx.Configuration["BaseDatos:CadenaConexion"] ?? "Data Source=swdata.db";
                services.AddAppDbContext(cadena, ServiceLifetime.Scoped);

                // DocManager primero: para alcance Rápida, el orquestador lo intenta primero
                // (no requiere SW abierto, solo la clave de licencia). SwApi segundo: único
                // capaz de cubrir Profunda (features/roscas), y sirve de sustituto liviano de
                // DocManager para Rápida cuando no hay licencia configurada (ver OrquestadorExtraccion).
                // La clave se lee de la BD (configurable desde Configuración → sin terminal);
                // si no hay ninguna guardada, cae a IConfiguration (user-secrets/appsettings).
                services.AddSingleton<IExtractorCad>(sp =>
                    new ExtractorDocManager(ObtenerClaveDocManager(sp, ctx.Configuration)));
                // StepHeader antes que SwApi: para .stp/.step lee el encabezado ISO-10303-21
                // sin SW ni licencia; SwApi queda de respaldo (y único para alcance Profunda).
                services.AddSingleton<IExtractorCad, SWDataExtractor.Application.Servicios.ExtractorStep>();
                services.AddSingleton<IExtractorCad, ExtractorSwApi>();

                services.AddScoped<AppEscan>();
                services.AddScoped<AppOrq>();
                services.AddScoped<AppBom>();
                services.AddScoped<SWDataExtractor.Application.Servicios.ServicioAnalisisProyecto>();
                services.AddScoped<IEscritorPropiedades>(sp =>
                    new EscritorDocManager(ObtenerClaveDocManager(sp, ctx.Configuration)));
                services.AddScoped<AppProps>();

                services.AddScoped<AppTarea>();
                services.AddScoped<AppRoles>();
                services.AddScoped<AppLicencias>();
                services.AddSingleton<SWDataExtractor.UI.Servicios.ServicioNotificaciones>();
                services.AddScoped<SWDataExtractor.UI.Servicios.ServicioTema>();
                services.AddSingleton<FuncionalidadesViewModel>();
                services.AddTransient<SWDataExtractor.UI.Views.ConfiguracionWindow>();

                services.AddScoped<ArchivosViewModel>();
                services.AddScoped<DetalleArchivoViewModel>();
                services.AddScoped<ExploradorViewModel>();
                services.AddScoped<ColaTrabajoViewModel>();
                services.AddScoped<HistorialViewModel>();
                services.AddScoped<MainViewModel>();

                services.AddScoped<ArchivosView>();
                services.AddScoped<DetalleArchivoView>();
                services.AddScoped<ExploradorView>();
                services.AddScoped<ColaTrabajoView>();
                services.AddScoped<HistorialView>();
                services.AddScoped<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var cadenaInicio = _host.Services.GetRequiredService<IConfiguration>()["BaseDatos:CadenaConexion"] ?? "Data Source=swdata.db";
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DbContextExtensions.InicializarAsync(db, cadenaInicio);
        }

        // Cargar rol persistido y aplicar funcionalidades
        var funcVm = _host.Services.GetRequiredService<FuncionalidadesViewModel>();
        using (var scope = _host.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<AppRoles>();
            var rol   = await roles.ObtenerRolAsync();
            funcVm.Aplicar(rol, AppRoles.FuncionalidadesParaRol(rol));
        }

        // Aplicar el tema guardado (Plano técnico claro / Consola oscuro) antes de mostrar la
        // ventana, para no arrancar con el tema por defecto y saltar al elegido después.
        using (var scope = _host.Services.CreateScope())
        {
            var tema = scope.ServiceProvider.GetRequiredService<SWDataExtractor.UI.Servicios.ServicioTema>();
            var guardado = await tema.ObtenerTemaGuardadoAsync();
            tema.Aplicar(guardado);
        }

        var ventana = _host.Services.GetRequiredService<MainWindow>();
        MainWindow  = ventana;
        ventana.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        Serilog.Log.CloseAndFlush();
        _instanciaUnica?.Dispose();
        base.OnExit(e);
    }

    // Excepciones no controladas: registrar SIEMPRE en el log y, cuando sea recuperable
    // (hilo de UI), avisar al usuario sin tumbar la aplicación.
    private void RegistrarManejoGlobalDeExcepciones()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Serilog.Log.Error(args.Exception, "Excepción no controlada en el hilo de UI");
            MessageBox.Show(
                $"Ocurrió un error inesperado:\n\n{args.Exception.Message}\n\n" +
                $"Se registró el detalle en:\n{CarpetaLogs}",
                "SWDataExtractor — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // la app sigue viva; el usuario decide si continuar
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Serilog.Log.Fatal(args.ExceptionObject as Exception,
                "Excepción no controlada fuera del hilo de UI (la aplicación terminará)");
            Serilog.Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Serilog.Log.Error(args.Exception, "Excepción no observada en tarea en segundo plano");
            args.SetObserved();
        };
    }

    // Se llama al resolver los singletons IExtractorCad/IEscritorPropiedades de DocManager,
    // ya con la BD inicializada (ocurre en la primera resolución, tras InicializarAsync).
    // BD primero (configurable desde Configuración, sin terminal); IConfiguration de fallback
    // (user-secrets/appsettings, para desarrollo o si aún no se guardó nada en la BD).
    private static string ObtenerClaveDocManager(IServiceProvider sp, IConfiguration config)
    {
        try
        {
            using var scope = sp.CreateScope();
            var licencias = scope.ServiceProvider.GetRequiredService<AppLicencias>();
            var claveBd = licencias.ObtenerClaveDocManagerAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(claveBd)) return claveBd;
        }
        catch { /* BD no disponible todavía; usar el fallback de configuración */ }
        return config["DocManager:LicenciaKey"] ?? "";
    }
}
