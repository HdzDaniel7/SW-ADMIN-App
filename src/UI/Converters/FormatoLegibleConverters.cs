using System.Globalization;
using System.Windows.Data;

namespace SWDataExtractor.UI.Converters;

// ISO 8601 ("2026-07-03T17:28:34.84...") → "03/07/2026 17:28" en hora local.
public class FechaLegibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string iso || string.IsNullOrEmpty(iso)) return value;
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var f)
            ? f.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : iso;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Bytes → "1.2 MB" / "845 KB" / "912 B".
public class TamanoLegibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            if (value is int i) bytes = i;
            else return value;
        }
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):N1} GB",
            >= 1024 * 1024        => $"{bytes / (1024.0 * 1024):N1} MB",
            >= 1024               => $"{bytes / 1024.0:N0} KB",
            _                     => $"{bytes} B"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
