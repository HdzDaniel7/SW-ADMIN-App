using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// Compara un índice entero (p. ej. SeccionActiva del sidebar) contra el ConverterParameter
// para decidir si el panel correspondiente debe mostrarse. Usado para simular content-swap
// sin depender de Frame/Page (ver DECISIONES.md sobre por qué se evitó NavigationView).
public class IndiceAVisibilidadConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int indiceActual) return Visibility.Collapsed;
        if (parameter is not string texto || !int.TryParse(texto, out int indiceEsperado))
            return Visibility.Collapsed;
        return indiceActual == indiceEsperado ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
