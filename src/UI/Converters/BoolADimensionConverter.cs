using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// Colapsa columnas de un Grid según un bool (usado para ocultar por completo el panel de
// detalle y su separador cuando no hay archivo seleccionado — así la grilla de archivos
// dispone de todo el ancho al abrir la app, en vez de reservar espacio para un panel vacío).
// ConverterParameter: "detalle" (1* / 0), "separador" (6px / 0), "minimo" (240.0 / 0.0).
public class BoolADimensionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is true;
        return (parameter as string) switch
        {
            "separador" => visible ? new GridLength(6) : new GridLength(0),
            "minimo"    => visible ? 240d : 0d,
            _           => visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
