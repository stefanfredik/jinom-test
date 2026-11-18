using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FoTestingApp.Models;
using MaterialDesignThemes.Wpf;

namespace FoTestingApp.Helpers;

/// <summary>
/// How a test status looks everywhere — the same M3 status colours as jinom-assets (PASS = online,
/// PARTIAL = warning, FAIL = offline), always with an icon and a word, never colour alone.
/// </summary>
public sealed record StatusVisual(string Label, string AccentKey, string ContainerKey, string OnContainerKey, PackIconKind Icon)
{
    public static readonly StatusVisual Pass = new("PASS", "StatusOnlineBrush", "StatusOnlineContainerBrush", "StatusOnlineOnContainerBrush", PackIconKind.CheckCircle);
    public static readonly StatusVisual Partial = new("PARTIAL", "StatusWarningBrush", "StatusWarningContainerBrush", "StatusWarningOnContainerBrush", PackIconKind.AlertCircle);
    public static readonly StatusVisual Fail = new("FAIL", "StatusOfflineBrush", "StatusOfflineContainerBrush", "StatusOfflineOnContainerBrush", PackIconKind.CloseCircle);
    public static readonly StatusVisual Running = new("BERJALAN", "InfoBrush", "InfoContainerBrush", "OnInfoContainerBrush", PackIconKind.TimerSand);

    public static StatusVisual Of(TestStatus status) => status == TestStatus.Pass ? Pass : Fail;

    public static StatusVisual Of(bool passed) => passed ? Pass : Fail;

    public static StatusVisual Of(TestOverallStatus status) => status switch
    {
        TestOverallStatus.Pass => Pass,
        TestOverallStatus.Partial => Partial,
        _ => Fail,
    };
}

/// <summary>
/// The pieces screens build in code (result rows, status chips, dialogs, snackbars), made from the
/// same roles and type scale as the XAML. Every colour is a resource reference, so a theme switch
/// repaints them too.
/// </summary>
public static class Ui
{
    /// <summary>A text in a type-scale style ("Text.BodyMedium"), optionally in a role colour.</summary>
    public static TextBlock Text(string text, string style, string? brushKey = null)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (brushKey is not null)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }
        return block;
    }

    /// <summary>A status as a chip: icon and word on the status container.</summary>
    public static Border StatusChip(StatusVisual status)
    {
        var icon = new PackIcon { Kind = status.Icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        icon.SetResourceReference(Control.ForegroundProperty, status.OnContainerKey);
        var label = Text(status.Label, "Text.LabelLarge", status.OnContainerKey);
        label.VerticalAlignment = VerticalAlignment.Center;

        var chip = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
        };
        chip.SetResourceReference(Border.BackgroundProperty, status.ContainerKey);
        return chip;
    }

    /// <summary>
    /// One result as an M3 list row: a status-tinted leading icon, the test and what it found, and its
    /// status chip. Used for the live log while tests run.
    /// </summary>
    public static Border ResultRow(string title, string supporting, StatusVisual status, PackIconKind icon)
    {
        var lead = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
        lead.SetResourceReference(Border.BackgroundProperty, status.ContainerKey);
        var leadIcon = new PackIcon { Kind = icon, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        leadIcon.SetResourceReference(Control.ForegroundProperty, status.OnContainerKey);
        lead.Child = leadIcon;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, "Text.BodyLarge"));
        text.Children.Add(Text(supporting, "Text.Supporting"));

        var chip = StatusChip(status);
        chip.Margin = new Thickness(16, 0, 0, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(chip, 2);
        grid.Children.Add(lead);
        grid.Children.Add(text);
        grid.Children.Add(chip);

        var row = new Border { Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid, Opacity = 0 };
        row.SetResourceReference(Border.BorderBrushProperty, "OutlineVariantBrush");
        // A short fade as each result arrives (M3 standard easing); nothing decorative beyond that.
        row.Loaded += (_, _) => row.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        return row;
    }

    // ── Snackbar ────────────────────────────────────────────────────────────────

    /// <summary>The app's snackbar queue (shown at the bottom of the main window).</summary>
    public static SnackbarMessageQueue Messages { get; } = new(TimeSpan.FromSeconds(4));

    /// <summary>A short message at the bottom of the window: what happened, never a question.</summary>
    public static void Notify(string message) => Messages.Enqueue(message);

    // ── Dialogs ─────────────────────────────────────────────────────────────────

    /// <summary>The DialogHost identifier of the main window.</summary>
    public const string RootDialog = "RootDialog";

    /// <summary>
    /// An M3 dialog: headline, supporting text, and actions at the end — the confirming one last.
    /// True when confirmed. Falls back to a message box where no dialog host is open (login).
    /// </summary>
    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Batal", bool destructive = false)
    {
        if (!HasRootHost() || DialogHost.IsDialogOpen(RootDialog))
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, destructive ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        var result = await DialogHost.Show(BuildDialog(title, message, confirmText, cancelText, destructive), RootDialog);
        return result is true;
    }

    /// <summary>An M3 dialog with one action, for something the user must read (a validation list).</summary>
    public static async Task AlertAsync(string title, string message, string closeText = "Mengerti")
    {
        if (!HasRootHost() || DialogHost.IsDialogOpen(RootDialog))
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await DialogHost.Show(BuildDialog(title, message, closeText, cancelText: null, destructive: false), RootDialog);
    }

    /// <summary>The main window (which holds the dialog host) is up; the login window has none.</summary>
    private static bool HasRootHost() =>
        Application.Current.Windows.OfType<Views.MainShell>().Any(w => w.IsLoaded);

    private static FrameworkElement BuildDialog(string title, string message, string confirmText, string? cancelText, bool destructive)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        if (cancelText is not null)
        {
            actions.Children.Add(DialogButton(cancelText, result: false, "M3.TextButton"));
        }
        var confirm = DialogButton(confirmText, result: true, destructive ? "M3.ErrorTextButton" : "M3.TextButton");
        confirm.IsDefault = true;
        confirm.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(confirm);

        var headline = Text(title, "Text.HeadlineSmall");
        headline.Margin = new Thickness(0, 0, 0, 16);
        return new StackPanel
        {
            Width = 400,
            Margin = new Thickness(24),
            Children = { headline, Text(message, "Text.Supporting"), actions },
        };
    }

    private static Button DialogButton(string text, bool result, string style)
    {
        var button = new Button
        {
            Content = text,
            Command = DialogHost.CloseDialogCommand,
            CommandParameter = result,
            IsCancel = !result,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        return button;
    }
}
