using System.Windows;
using System.Windows.Controls;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class ExploradorView : UserControl
{
    private readonly ExploradorViewModel _vm;

    public ExploradorView(ExploradorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm         = vm;

        // Primer análisis al mostrar la sección (una sola vez; después con "Actualizar").
        Loaded += (_, _) =>
        {
            if (!_vm.YaCargado && !_vm.EstaCargando)
                _vm.ActualizarCommand.Execute(null);
        };
    }

    // TreeView.SelectedItem es de solo lectura — no se puede bindear TwoWay; se sincroniza aquí.
    private void ArbolCarpetas_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is NodoCarpeta nodo)
            _vm.CarpetaSeleccionada = nodo;
    }

    // Doble clic en cualquier grilla de reportes → navegar al detalle del archivo en la
    // sección Archivos (los reportes dejan de ser "sin salida").
    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        int? id = (sender as System.Windows.Controls.DataGrid)?.SelectedItem switch
        {
            ArchivoResumen a          => a.Id,
            FilaDuplicado d           => d.Id,
            FilaVersion v             => v.Id,
            FilaBomProyecto b         => b.Item.ArchivoId,
            IncumplimientoPropiedad i => i.ArchivoId,
            _                         => null
        };
        if (id is not null)
            _vm.SolicitarVerDetalle(id.Value);
    }
}
