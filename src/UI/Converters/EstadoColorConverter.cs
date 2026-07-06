using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SWDataExtractor.UI.Converters;

// Colores semánticos de estado (información, no marca — fijos en ambos temas), en tonos
// medios legibles tanto sobre fondo claro como oscuro. El caso desconocido devuelve
// UnsetValue para que el texto herede el Foreground del tema en vez de un negro fijo
// (que en modo oscuro era ilegible).
public class EstadoColorConverter : IValueConverter
{
    private static readonly Brush Ok         = Congelar("#2FA36B");
    private static readonly Brush Pendiente  = Congelar("#3F8CD8");
    private static readonly Brush EnProceso  = Congelar("#D9822B");
    private static readonly Brush Error      = Congelar("#E5484D");
    private static readonly Brush Advertencia = Congelar("#C77D1F");
    private static readonly Brush Bloqueado  = Congelar("#C0392B");
    private static readonly Brush Omitido    = Congelar("#8A94A0");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "ok"                                 => Ok,
            "pendiente"                          => Pendiente,
            "en_proceso"                         => EnProceso,
            "error" or "timeout" or "error_otro" => Error,
            "version_no_soportada"               => Advertencia,
            "bloqueado" or "error_bloqueado"     => Bloqueado,
            "omitido"                            => Omitido,
            _                                    => DependencyProperty.UnsetValue
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Congelar(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
