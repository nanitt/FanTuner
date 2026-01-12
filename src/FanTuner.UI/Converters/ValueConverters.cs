using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FanTuner.UI.Converters;

/// <summary>
/// Converts temperature value to a level (Normal, High, Critical)
/// </summary>
public class TemperatureToLevelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double temp or float)
        {
            var temperature = System.Convert.ToDouble(value);

            if (temperature >= 85)
                return "Critical";
            if (temperature >= 70)
                return "High";
            return "Normal";
        }

        return "Normal";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            bool invert = parameter?.ToString()?.ToLower() == "invert";
            if (invert)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }

        return false;
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return false;
    }
}

/// <summary>
/// Converts percentage (0-100) to width based on container width
/// </summary>
public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is double percent &&
            values[1] is double containerWidth)
        {
            return containerWidth * (percent / 100.0);
        }

        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts fan control capability to status text
/// </summary>
public class CapabilityToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FanTuner.Core.Models.FanControlCapability capability)
        {
            return capability switch
            {
                FanTuner.Core.Models.FanControlCapability.FullControl => "Controllable",
                FanTuner.Core.Models.FanControlCapability.MonitorOnly => "Monitor Only",
                FanTuner.Core.Models.FanControlCapability.Unavailable => "Unavailable",
                _ => "Unknown"
            };
        }

        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts fan control mode to display text
/// </summary>
public class ModeToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FanTuner.Core.Models.FanControlMode mode)
        {
            return mode switch
            {
                FanTuner.Core.Models.FanControlMode.Auto => "Automatic",
                FanTuner.Core.Models.FanControlMode.Manual => "Manual",
                FanTuner.Core.Models.FanControlMode.Curve => "Curve",
                _ => "Unknown"
            };
        }

        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Formats temperature with unit
/// </summary>
public class TemperatureFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float or double)
        {
            var temp = System.Convert.ToDouble(value);
            return $"{temp:F1}°C";
        }

        return "N/A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Formats RPM value
/// </summary>
public class RpmFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float or double)
        {
            var rpm = System.Convert.ToDouble(value);
            return $"{rpm:F0} RPM";
        }

        return "N/A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Formats percentage value
/// </summary>
public class PercentFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float or double or int)
        {
            var percent = System.Convert.ToDouble(value);
            return $"{percent:F0}%";
        }

        return "N/A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
