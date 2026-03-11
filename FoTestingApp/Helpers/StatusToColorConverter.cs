using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FoTestingApp.Models;

namespace FoTestingApp.Helpers;

public class StatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TestOverallStatus status)
        {
            return status switch
            {
                TestOverallStatus.Pass => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5")), // Emerald 100
                TestOverallStatus.Fail => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2")), // Red 100
                TestOverallStatus.Partial => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7")), // Amber 100
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")) // Slate 100
            };
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TestOverallStatus status)
        {
            return status switch
            {
                TestOverallStatus.Pass => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")), // Emerald 600
                TestOverallStatus.Fail => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")), // Red 600
                TestOverallStatus.Partial => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")), // Amber 600
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")) // Slate 600
            };
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
