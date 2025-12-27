using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using MotorcycleRAG.MobileApp.Models;

namespace MotorcycleRAG.MobileApp.Converters
{
    public class SenderToHorizontalOptionsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MessageSender sender)
            {
                return sender == MessageSender.User ? LayoutOptions.End : LayoutOptions.Start;
            }
            return LayoutOptions.Start;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
