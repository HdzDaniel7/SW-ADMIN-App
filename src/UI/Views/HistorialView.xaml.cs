using System.Windows.Controls;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class HistorialView : UserControl
{
    public HistorialView(HistorialViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
