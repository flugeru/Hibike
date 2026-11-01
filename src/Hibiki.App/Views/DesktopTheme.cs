using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibiki.App.Views;

/// <summary>
/// Desktop-specific visual system, informed by OrbisExtract's restrained cards,
/// consistent spacing, and three clearly separated dark surfaces.
/// Does not change Android's resource dictionary.
/// </summary>
internal static class DesktopTheme
{
    public static readonly IBrush Background = Brush.Parse("#000000");
    public static readonly IBrush Sidebar = Brush.Parse("#0B0C11");
    public static readonly IBrush Surface = Brush.Parse("#11141B");
    public static readonly IBrush Raised = Brush.Parse("#1A1F29");
    public static readonly IBrush Outline = Brush.Parse("#2B3241");
    public static readonly IBrush Accent = Brush.Parse("#B9A7FF");
    public static readonly IBrush AccentSoft = Brush.Parse("#28233D");
    public static readonly IBrush TextPrimary = Brush.Parse("#F5F6FA");
    public static readonly IBrush TextSecondary = Brush.Parse("#AFB7C8");
    public static readonly IBrush TextMuted = Brush.Parse("#8792A6");
    public static readonly IBrush OnAccent = Brush.Parse("#19122C");

    public static TextBlock Text(string value, double size = 14, IBrush? color = null,
        FontWeight weight = FontWeight.Normal) => new()
        {
            Text = value, FontSize = size, Foreground = color ?? TextPrimary,
            FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis
        };

    public static Border Card(Control content, double padding = 20, IBrush? background = null) => new()
    {
        Background = background ?? Surface,
        BorderBrush = Outline,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(padding),
        Child = content
    };

    public static Button Button(string label, bool primary = false) => new()
    {
        Content = label,
        MinHeight = 40, Height = 40,
        FontSize = 13, FontWeight = FontWeight.SemiBold,
        Padding = new Thickness(14, 6),
        CornerRadius = new CornerRadius(10),
        Background = primary ? Accent : Raised,
        Foreground = primary ? OnAccent : TextPrimary,
        BorderBrush = primary ? Accent : Outline,
        BorderThickness = new Thickness(primary ? 0 : 1),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    public static Button Icon(string icon, string hint, int size = 20, bool accent = false)
    {
        var button = Button("", primary: false);
        button.MinWidth = 38; button.Width = 38; button.Height = 38;
        button.Padding = new Thickness(5);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Content = Icons.Make(icon, accent ? Accent : TextSecondary, size);
        ToolTip.SetTip(button, hint);
        Avalonia.Automation.AutomationProperties.SetName(button, hint);
        return button;
    }

    public static Border Separator() => new()
    {
        Height = 1, Background = Outline,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
}
