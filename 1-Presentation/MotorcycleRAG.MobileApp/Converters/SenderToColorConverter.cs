using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using MotorcycleRAG.MobileApp.Models;

namespace MotorcycleRAG.MobileApp.Converters
{
    public class SenderToColorConverter : IValueConverter
    {
        public Color UserColor { get; set; } = Colors.LightBlue;
        public Color SystemColor { get; set; } = Colors.LightGray;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is MessageSender sender)
            {
                return sender == MessageSender.User ? UserColor : SystemColor;
            }
            return SystemColor;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
