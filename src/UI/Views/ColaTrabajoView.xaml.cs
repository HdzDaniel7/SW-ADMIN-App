using System.Windows.Controls;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Views;

public partial class ColaTrabajoView : UserControl
{
    public ColaTrabajoView(ColaTrabajoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
