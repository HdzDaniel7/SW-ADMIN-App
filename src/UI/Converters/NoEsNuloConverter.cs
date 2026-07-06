using System.Globalization;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// true si el valor no es null — para IsEnabled de botones que requieren una selección.
public class NoEsNuloConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
