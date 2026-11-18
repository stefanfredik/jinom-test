using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FoTestingApp.Models;

namespace FoTestingApp.Helpers;

/// <summary>
/// A session's overall status as its status-container colour (the chip behind the word). Read from
/// the current theme's roles; a list re-binds after a theme switch (ThemeService.Changed).
/// </summary>
public class StatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TestOverallStatus status
            ? Application.Current.TryFindResource(StatusVisual.Of(status).ContainerKey) as Brush ?? Brushes.Transparent
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A session's overall status as the text colour on its status container.</summary>
public class StatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TestOverallStatus status
            ? Application.Current.TryFindResource(StatusVisual.Of(status).OnContainerKey) as Brush ?? SystemColors.ControlTextBrush
            : SystemColors.ControlTextBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A session's overall status as its icon (never colour alone).</summary>
public class StatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TestOverallStatus status ? StatusVisual.Of(status).Icon : MaterialDesignThemes.Wpf.PackIconKind.HelpCircleOutline;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
