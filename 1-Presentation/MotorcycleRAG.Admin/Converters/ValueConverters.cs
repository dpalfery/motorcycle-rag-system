#pragma warning disable CA1812 // Members are never instantiated - kept for potential future use
#pragma warning disable CA1852 // Types can be sealed but kept as-is

using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MotorcycleRAG.Admin.Converters;

/// <summary>
/// Converts a boolean value to its inverse
/// </summary>
internal class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Convert(value, targetType, parameter, culture);
    }
}

/// <summary>
/// Checks if a string is not null or empty
/// </summary>
internal class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string str && !string.IsNullOrWhiteSpace(str);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("StringNotEmptyConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a percentage (0-100) to a progress value (0-1)
/// </summary>
internal class PercentageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
            return doubleValue / 100.0;
        if (value is int intValue)
            return intValue / 100.0;
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
            return doubleValue * 100.0;
        return 0.0;
    }
}

/// <summary>
/// Checks if a nullable value has a value
/// </summary>
internal class HasValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return false;

        // Check for nullable types
        var type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return true;
        }

        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("HasValueConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a string to visibility (visible if not empty)
/// </summary>
internal class StringToVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string str && !string.IsNullOrWhiteSpace(str);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("StringToVisibleConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a boolean enabled state to a color (green if enabled, gray if disabled)
/// </summary>
internal class EnabledToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Colors.Green : Colors.Gray;
        }
        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("EnabledToColorConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a boolean enabled state to a display status string
/// </summary>
internal class StatusLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "Enabled" : "Disabled";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("StatusLabelConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a status value to a categorical color
/// </summary>
internal class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Colors.Green : Colors.Gray;
        }
        
        if (value is string status)
        {
            return status.ToUpperInvariant() switch
            {
                "ENABLED" or "ACTIVE" or "SUCCESS" or "COMPLETED" => Colors.Green,
                "DISABLED" or "INACTIVE" or "FAILED" or "ERROR" => Colors.Red,
                "PENDING" or "RUNNING" or "PROCESSING" => Colors.Blue,
                "WARNING" => Colors.Orange,
                _ => Colors.Gray
            };
        }
        
        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("StatusColorConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a boolean value to different objects based on parameter
/// </summary>
internal class BoolToObjectConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramString)
        {
            var parts = paramString.Split('|');
            var falseValue = parts.Length > 1 ? parts[1] : parts[0];
            return boolValue ? parts[0] : falseValue;
        }
        return parameter;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("BoolToObjectConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a string to boolean (true if not null or empty)
/// </summary>
internal class StringToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string str && !string.IsNullOrWhiteSpace(str);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("StringToBoolConverter does not support two-way binding.");
    }
}

/// <summary>
/// Converts a boolean to a color (green if true, red if false)
/// Used for validation indicators.
/// </summary>
internal class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Colors.Green : Colors.Red;
        }
        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("BoolToColorConverter does not support two-way binding.");
    }
}
