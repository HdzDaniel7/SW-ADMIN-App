using System.Windows;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class PropiedadLoteWindow : Wpf.Ui.Controls.FluentWindow
{
    public PropiedadLoteWindow(PropiedadLoteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
