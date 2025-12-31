using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MotorcycleRAG.MobileApp.Converters
{
    public class IntToBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0;
            }
            if (value is string str)
            {
                return !string.IsNullOrEmpty(str);
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InvertedBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }
            return false;
        }
    }
}
