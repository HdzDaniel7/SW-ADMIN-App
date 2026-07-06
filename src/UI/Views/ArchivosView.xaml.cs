using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class ArchivosView : UserControl
{
    private readonly ArchivosViewModel    _vm;
    private readonly ServicioPropiedades  _servicioPropiedades;
    private readonly AppDbContext         _db;

    public ArchivosView(ArchivosViewModel vm, ServicioPropiedades servicioPropiedades, AppDbContext db,
        DetalleArchivoView detalleView)
    {
        InitializeComponent();
        DataContext          = vm;
        _vm                  = vm;
        _servicioPropiedades = servicioPropiedades;
        _db                  = db;

        // El panel de detalle vive embebido aquí (maestro-detalle) en vez de en una pestaña
        // aparte: se actualiza solo al cambiar la selección, sin navegar a otra pantalla.
        DetallePresenter.Content = detalleView;

        // Cargar la lista al abrir la app (antes el grid arrancaba vacío hasta "Recargar").
        Loaded += (_, _) =>
        {
            if (_vm.ArchivosVista is null)
                _vm.CargarCommand.Execute(null);
        };
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dg) return;
        _vm.ArchivosSeleccionados.Clear();
        foreach (var item in dg.SelectedItems.OfType<Archivo>())
            _vm.ArchivosSeleccionados.Add(item);
    }

    // Doble clic en una fila abre el archivo con la aplicación asociada (SolidWorks si está
    // instalado) — patrón estándar de Explorador de Windows, resuelve la ambigüedad de
    // "¿cuál archivo estoy abriendo?" al hacerlo explícito sobre la fila bajo el cursor.
    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is Archivo archivo)
            _vm.AbrirArchivoCommand.Execute(archivo);
    }

    // Clic derecho selecciona la fila bajo el cursor antes de abrir el menú contextual
    // (WPF no lo hace por defecto y el menú operaría sobre la selección anterior).
    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var origen = e.OriginalSource as DependencyObject;
        while (origen is not null && origen is not DataGridRow)
            origen = System.Windows.Media.VisualTreeHelper.GetParent(origen);
        if (origen is DataGridRow fila)
            fila.IsSelected = true;
    }

    private async void EditarPropiedades_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.ArchivosSeleccionados.Any()) return;
        var rutas = _vm.ArchivosSeleccionados.Select(a => a.Ruta).ToList();
        var loteVm = new PropiedadLoteViewModel(_servicioPropiedades, _db, rutas);
        await loteVm.CargarDiccionarioAsync();
        var ventana = new PropiedadLoteWindow(loteVm) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
