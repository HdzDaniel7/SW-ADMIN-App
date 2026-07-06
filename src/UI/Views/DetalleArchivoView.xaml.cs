using System.Windows.Controls;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class DetalleArchivoView : UserControl
{
    public DetalleArchivoView(DetalleArchivoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
