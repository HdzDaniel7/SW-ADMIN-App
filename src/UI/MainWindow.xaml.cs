using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SWDataExtractor.UI.Servicios;
using SWDataExtractor.UI.ViewModels;
using SWDataExtractor.UI.Views;

namespace SWDataExtractor.UI;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly DetalleArchivoViewModel _detalle;
    private readonly IServiceScopeFactory _scopeFactory;

    public MainWindow(MainViewModel vm, ArchivosView archivosView,
                      ExploradorView exploradorView,
                      ColaTrabajoView colaView, HistorialView historialView,
                      DetalleArchivoViewModel detalle,
                      IServiceScopeFactory scopeFactory,
                      ServicioNotificaciones notificaciones)
    {
        InitializeComponent();
        DataContext    = vm;
        _vm            = vm;
        _detalle       = detalle;
        _scopeFactory  = scopeFactory;

        // Inyectar las vistas en los placeholders del XAML. Detalle ya no es una vista
        // aparte: ArchivosView la aloja como panel maestro-detalle (ver ArchivosView.xaml.cs).
        ArchivosViewControl.Content   = archivosView;
        ExploradorViewControl.Content = exploradorView;
        ColaViewControl.Content       = colaView;
        HistorialViewControl.Content  = historialView;

        // Conecta el SnackbarPresenter de esta ventana al servicio (Singleton) para que
        // cualquier ViewModel/ventana pueda disparar notificaciones sin conocer MainWindow.
        notificaciones.Presenter = RootSnackbar;

        // Recordar tamaño/posición/maximizado entre sesiones (clave "VentanaPrincipal" en
        // ajustes_app). Se aplica en Loaded (BD ya inicializada) y se guarda al cerrar.
        Loaded  += async (_, _) => await RestaurarVentanaAsync();
        Closing += (_, _) => GuardarVentana();
    }

    private async Task RestaurarVentanaAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SWDataExtractor.Data.AppDbContext>();
            var ajuste = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.AjustesApp, a => a.Clave == "VentanaPrincipal");
            var partes = ajuste?.Valor?.Split(';');
            if (partes is not { Length: 5 }) return;

            double w = double.Parse(partes[0]), h = double.Parse(partes[1]);
            double x = double.Parse(partes[2]), y = double.Parse(partes[3]);

            // Solo aplicar si la posición cae dentro del escritorio virtual actual
            // (evita ventanas perdidas al desconectar un monitor).
            var pantalla = SystemParameters.VirtualScreenWidth;
            var pantallaAlto = SystemParameters.VirtualScreenHeight;
            if (x >= -w + 100 && x < pantalla - 100 && y >= 0 && y < pantallaAlto - 100)
            {
                Width = w; Height = h; Left = x; Top = y;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            if (partes[4] == "1") WindowState = WindowState.Maximized;
        }
        catch { /* preferencia de UI: nunca impedir el arranque */ }
    }

    private void GuardarVentana()
    {
        try
        {
            var b = RestoreBounds; // dimensiones sin maximizar
            var valor = string.Join(';',
                b.Width, b.Height, b.Left, b.Top,
                WindowState == WindowState.Maximized ? "1" : "0");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SWDataExtractor.Data.AppDbContext>();
            var ajuste = db.AjustesApp.FirstOrDefault(a => a.Clave == "VentanaPrincipal");
            if (ajuste is null)
                db.AjustesApp.Add(new SWDataExtractor.Data.Entities.AjusteApp
                {
                    Clave = "VentanaPrincipal", Valor = valor,
                    Descripcion = "Tamaño/posición de la ventana principal (W;H;X;Y;Max)"
                });
            else
                ajuste.Valor = valor;
            db.SaveChanges();
        }
        catch { /* preferencia de UI: nunca impedir el cierre */ }
    }

    private void ExportarPropiedades_Click(object sender, RoutedEventArgs e) =>
        _detalle.ExportarPropiedadesCommand.Execute(null);

    private void Salir_Click(object sender, RoutedEventArgs e) => Close();

    private void Configuracion_Click(object sender, RoutedEventArgs e)
    {
        using var scope = _scopeFactory.CreateScope();
        var ventana = scope.ServiceProvider.GetRequiredService<ConfiguracionWindow>();
        ventana.Owner = this;
        ventana.ShowDialog();
    }

    // Crea un .lnk en el escritorio apuntando al exe actual — pensado para el flujo
    // "descargo el zip, abro el exe, un clic y ya tengo el acceso directo".
    private void CrearAccesoDirecto_Click(object sender, RoutedEventArgs e)
    {
        object? shell = null;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                MessageBox.Show("No se pudo determinar la ruta del ejecutable.", "Acceso directo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var escritorio = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var destino    = System.IO.Path.Combine(escritorio, "SWDataExtractor.lnk");

            var tipo = Type.GetTypeFromProgID("WScript.Shell")
                       ?? throw new InvalidOperationException("WScript.Shell no disponible.");
            shell = Activator.CreateInstance(tipo)!;
            dynamic acceso = ((dynamic)shell).CreateShortcut(destino);
            acceso.TargetPath       = exe;
            acceso.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);
            acceso.IconLocation     = exe + ",0";
            acceso.Description      = "SWDataExtractor — extracción y gestión de datos SolidWorks";
            acceso.Save();
            System.Runtime.InteropServices.Marshal.ReleaseComObject(acceso);

            MessageBox.Show("Acceso directo creado en el escritorio.", "SWDataExtractor",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo crear el acceso directo: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (shell is not null)
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    private void AcercaDe_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        MessageBox.Show(
            $"SWDataExtractor v{version?.ToString(3)}\n" +
            "Extracción y gestión de datos de archivos SolidWorks.",
            "Acerca de", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
