using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.UI.Servicios;
using Wpf.Ui.Appearance;

namespace SWDataExtractor.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ServicioTema _tema;

    public ArchivosViewModel        Archivos    { get; }
    public DetalleArchivoViewModel  Detalle     { get; }
    public ColaTrabajoViewModel     ColaTrabajo { get; }
    public HistorialViewModel       Historial   { get; }

    // Sección activa del sidebar (0 = Archivos, 1 = Cola de trabajos, 2 = Historial).
    // El detalle ya no es una sección aparte: vive como panel maestro-detalle dentro de Archivos.
    [ObservableProperty] private int _seccionActiva;

    [ObservableProperty] private bool _temaOscuro;

    public MainViewModel(
        ArchivosViewModel archivos,
        DetalleArchivoViewModel detalle,
        ExploradorViewModel explorador,
        ColaTrabajoViewModel cola,
        HistorialViewModel historial,
        ServicioTema tema)
    {
        Archivos    = archivos;
        Detalle     = detalle;
        ColaTrabajo = cola;
        Historial   = historial;
        _tema       = tema;
        _temaOscuro = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        // Doble clic en un reporte del Explorador → saltar a la sección Archivos con ese
        // archivo seleccionado (y su panel de detalle abierto).
        explorador.VerDetalleSolicitado += async id =>
        {
            SeccionActiva = 0;
            await archivos.SeleccionarPorIdAsync(id);
        };

        // Selección del grid de Archivos actualiza el panel de detalle embebido en el
        // acto — sin navegar a ninguna otra pantalla (antes saltaba a una pestaña aparte,
        // lo que era confuso porque perdías de vista la lista al seleccionar).
        archivos.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(ArchivosViewModel.ArchivoSeleccionado)
                && archivos.ArchivoSeleccionado is Archivo a)
            {
                await detalle.CargarArchivoAsync(a);
            }
        };
    }

    // Alterna entre "Plano técnico" (claro) y "Consola" (oscuro) y lo recuerda para el
    // próximo arranque.
    [RelayCommand]
    private async Task AlternarTemaAsync()
    {
        var nuevo = TemaOscuro ? ApplicationTheme.Light : ApplicationTheme.Dark;
        _tema.Aplicar(nuevo);
        await _tema.GuardarTemaAsync(nuevo);
        TemaOscuro = nuevo == ApplicationTheme.Dark;

        // Regenerar las filas del grid bajo el tema nuevo: algunos recursos heredados dentro
        // de los contenedores de fila (selección, foreground) no se re-resuelven en caliente,
        // y quedaban con colores del tema anterior. Recargar restaura también la selección por
        // Id, lo que dispara además la recarga del panel de detalle bajo el tema nuevo.
        await Archivos.CargarCommand.ExecuteAsync(null);
    }
}
