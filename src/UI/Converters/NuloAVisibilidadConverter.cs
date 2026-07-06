using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// Visible si el valor no es null (usado para ocultar el recuadro de preview cuando no hay
// imagen, en vez de reservarle siempre un ancho fijo — ver DetalleArchivoView.xaml).
public class NuloAVisibilidadConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
