using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// Ruta completa del archivo → su carpeta contenedora. Clave de agrupación para
// "Agrupar por carpeta" en la grilla de Archivos.
public class CarpetaDeRutaConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is string ruta && !string.IsNullOrEmpty(ruta)
            ? Path.GetDirectoryName(ruta) ?? ruta
            : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
