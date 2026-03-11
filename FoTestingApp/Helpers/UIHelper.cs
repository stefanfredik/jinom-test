using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FoTestingApp.Helpers;

public static class UIHelper
{
    public static Border CreateLogCard(string title, string subtitle, SolidColorBrush primaryColor, SolidColorBrush bgColor, MaterialDesignThemes.Wpf.PackIconKind iconKind, string statusText)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon Box
        var iconBorder = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
            Background = bgColor, Margin = new Thickness(0, 0, 16, 0)
        };
        iconBorder.Child = new MaterialDesignThemes.Wpf.PackIcon
        {
            Kind = iconKind, Foreground = primaryColor, Width = 24, Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Text Stack
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock
        {
            Text = title, FontWeight = FontWeights.Bold, FontSize = 14
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextDarkBrush");
        var subtitleBlock = new TextBlock
        {
            Text = subtitle, FontSize = 12, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 2, 0, 0)
        };
        subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
        textStack.Children.Add(titleBlock);
        textStack.Children.Add(subtitleBlock);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Status Badge
        var badgeBorder = new Border
        {
            Background = bgColor, CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Top
        };
        badgeBorder.Child = new TextBlock
        {
            Text = statusText, Foreground = primaryColor, FontWeight = FontWeights.Black, FontSize = 10
        };
        Grid.SetColumn(badgeBorder, 2);
        grid.Children.Add(badgeBorder);

        var cardBorder = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(16)
        };
        cardBorder.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        cardBorder.Child = grid;

        return cardBorder;
    }
}
