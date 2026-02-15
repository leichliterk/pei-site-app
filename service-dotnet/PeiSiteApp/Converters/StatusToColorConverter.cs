using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PeiSiteApp.Models;

namespace PeiSiteApp.Converters;

public class ConnectionStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Connected => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // green
                ConnectionStatus.Connecting => new SolidColorBrush(Color.FromRgb(59, 130, 246)),  // blue
                ConnectionStatus.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),        // red
                _ => new SolidColorBrush(Color.FromRgb(156, 163, 175))                            // gray
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ConnectionStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Connected => new SolidColorBrush(Color.FromArgb(30, 34, 197, 94)),
                ConnectionStatus.Connecting => new SolidColorBrush(Color.FromArgb(30, 59, 130, 246)),
                ConnectionStatus.Error => new SolidColorBrush(Color.FromArgb(30, 239, 68, 68)),
                _ => new SolidColorBrush(Color.FromArgb(30, 156, 163, 175))
            };
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ConnectionStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Connected => "Connected",
                ConnectionStatus.Connecting => "Connecting...",
                ConnectionStatus.Error => "Error",
                _ => "Disconnected"
            };
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        return System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FtpStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FtpStatus status)
        {
            return status switch
            {
                FtpStatus.Connected => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                FtpStatus.Connecting => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                FtpStatus.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                FtpStatus.Disabled => new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                _ => new SolidColorBrush(Color.FromRgb(234, 179, 8))
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FtpStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FtpStatus status)
        {
            return status switch
            {
                FtpStatus.Connected => "Connected",
                FtpStatus.Connecting => "Connecting...",
                FtpStatus.Error => "Error",
                FtpStatus.Disabled => "Disabled",
                _ => "Disconnected"
            };
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToDashConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
            return s;
        return "--";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToServiceStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? "Running" : "Not Running";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToServiceColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
            : new SolidColorBrush(Color.FromRgb(234, 179, 8));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
            return System.Windows.Visibility.Visible;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringNotEmptyConverter : IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
