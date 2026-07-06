using System.Globalization;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// Negación simple de bool (ej.: ProgressBar determinada cuando hay lote en curso →
// IsIndeterminate = !LoteEnCurso).
public class BoolInversoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}
