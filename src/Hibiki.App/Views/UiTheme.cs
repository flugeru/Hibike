using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Hibiki.App.Views;

internal static class UiTheme
{
    public const string Background = "#0B0F14", Surface = "#151B24", SurfaceElevated = "#1D2530",
        Outline = "#2B3542", Primary = "#B9A7FF", PrimaryContainer = "#29243B", OnPrimary = "#1A1033",
        Success = "#71D6A3", Warning = "#FFCC80", Error = "#FF8A8A", TextPrimary = "#F5F7FA",
        TextSecondary = "#AAB4C3", TextDisabled = "#667181", Scrim = "#CC000000";
    public const double CardRadius = 18, ControlRadius = 12, ThumbRadius = 8, RoundRadius = 22,
        CompactGap = 8, SectionGap = 14, PageGap = 18, CardGap = 16, CardPadding = 20,
        PageTitle = 28, SectionTitle = 18, DialogTitle = 22, Body = 15, Caption = 13, Label = 12,
        ButtonText = 14, TouchSize = 48, IconSize = 24, IconStroke = 1.8, RoundSize = 44,
        ThumbnailSize = 52, MiniArtworkSize = 40, PlaySize = 72, TransportIconSize = 32,
        ProgressHeight = 8, ProgressRadius = 4, MiniProgressHeight = 4, SeekHeight = 6,
        SeekThumbSize = 16, SeekThumbPressedSize = 20, PageMaxWidth = 820, PlayerMaxWidth = 440,
        DialogMaxWidth = 520, CompactWidth = 390, DesktopWidth = 800, SidebarWidth = 200,
        NavHeight = 72, RowHeight = 76, MarkerSize = 7, EqualizerGap = 3, EqualizerBarWidth = 4, EqualizerStep = 5;
    public static readonly Thickness PageMargin = new(14, 14, 14, 18), ButtonPadding = new(16, 13),
        RowPadding = new(4, 8), ChipPadding = new(10, 4), BannerPadding = new(14, 10),
        One = new(1), Zero = new(0), InnerPadding = new(12), DialogMargin = new(14),
        MiniMargin = new(14, 0, 14, 8), TopBorder = new(0, 1, 0, 0), BottomBorder = new(0, 0, 0, 1);
    public static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();
    public static TextBlock Text(string? text, double size = Body, string color = TextPrimary,
        FontWeight weight = default) => new()
    {
        Text = text, FontFamily = Inter, FontSize = Math.Max(Label, size), FontWeight = weight,
        Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center
    };
    public static Border Card(Control child, double padding = CardPadding) => new()
    {
        Child = child, Background = Brush(Surface), BorderBrush = Brush(Outline), BorderThickness = One,
        CornerRadius = new(CardRadius), Padding = new(padding)
    };
    public static Border Chip(Control child) => new()
    {
        Child = child, Background = Brush(SurfaceElevated), BorderBrush = Brush(Outline),
        BorderThickness = One, CornerRadius = new(ThumbRadius), Padding = ChipPadding
    };
    public static Border Inner(Control child) => new()
    {
        Child = child, Background = Brush(SurfaceElevated), BorderBrush = Brush(Outline),
        BorderThickness = One, CornerRadius = new(ControlRadius), Padding = InnerPadding
    };
    public static void StateBanner(Border border, string color)
    {
        border.Background = new SolidColorBrush(Color.Parse(color), .12);
        border.BorderBrush = Brush(color); border.BorderThickness = One;
        border.CornerRadius = new(ControlRadius); border.Padding = BannerPadding;
    }
    public static Button PrimaryButton(string text) { var button = OutlinedButton(text); button.Classes.Add("primary"); return button; }
    public static Button OutlinedButton(string text) => new() { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static Button RoundIconButton(string icon, string label, string color = Primary)
    {
        var button = OutlinedButton(""); button.Classes.Add("round"); button.Content = Icons.Make(icon, color, IconSize);
        Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label);
        return button;
    }
    public static TextBox Field(string placeholder) => new()
    {
        PlaceholderText = placeholder, MinWidth = 0, VerticalContentAlignment = VerticalAlignment.Center
    };
    public static void Install(Styles styles)
    {
        void Add(Style style, params Setter[] setters) { foreach (var setter in setters) style.Setters.Add(setter); styles.Add(style); }
        Add(new Style(x => x.OfType<TemplatedControl>()), new Setter(TemplatedControl.FontFamilyProperty, Inter));
        Add(new Style(x => x.OfType<Button>()), new(TemplatedControl.CornerRadiusProperty, new CornerRadius(ControlRadius)),
            new(Layoutable.MinHeightProperty, TouchSize), new(Layoutable.MinWidthProperty, TouchSize),
            new(TemplatedControl.PaddingProperty, ButtonPadding), new(TemplatedControl.FontSizeProperty, ButtonText),
            new(TemplatedControl.ForegroundProperty, Brush(Primary)), new(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            new(TemplatedControl.BorderBrushProperty, Brush(Outline)), new(TemplatedControl.BorderThicknessProperty, One),
            new(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        Add(new Style(x => x.OfType<Button>().Class("primary")), new(TemplatedControl.BackgroundProperty, Brush(Primary)),
            new(TemplatedControl.ForegroundProperty, Brush(OnPrimary)), new(TemplatedControl.BorderThicknessProperty, Zero),
            new(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(new Style(x => x.OfType<Button>().Class("subtle")), new Setter(TemplatedControl.BorderThicknessProperty, Zero));
        Add(new Style(x => x.OfType<Button>().Class("round")), new(Layoutable.WidthProperty, TouchSize),
            new(Layoutable.HeightProperty, TouchSize), new(TemplatedControl.PaddingProperty, Zero),
            new(TemplatedControl.CornerRadiusProperty, new CornerRadius(RoundRadius)));
        Add(new Style(x => x.OfType<Button>().Class("nav")), new(TemplatedControl.BorderThicknessProperty, Zero), new(TemplatedControl.PaddingProperty, Zero));
        Add(new Style(x => x.OfType<Button>().Class(":disabled")), new Setter(Visual.OpacityProperty, .4));
        // Target Fluent's presenter so local backgrounds do not suppress pointer states.
        foreach (var state in new[] { ":pointerover", ":pressed" })
            Add(new Style(x => x.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
                new(ContentPresenter.BackgroundProperty, Brush(SurfaceElevated)), new(ContentPresenter.BorderBrushProperty, Brush(Primary)),
                new(ContentPresenter.ForegroundProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<TextBox>()), new(TemplatedControl.BackgroundProperty, Brush(Surface)),
            new(TemplatedControl.ForegroundProperty, Brush(TextPrimary)), new(TemplatedControl.BorderBrushProperty, Brush(Outline)),
            new(TemplatedControl.BorderThicknessProperty, One), new(TemplatedControl.CornerRadiusProperty, new CornerRadius(ControlRadius)),
            new(TemplatedControl.PaddingProperty, ButtonPadding), new(TemplatedControl.FontSizeProperty, Body), new(Layoutable.MinHeightProperty, TouchSize));
        Add(new Style(x => x.OfType<TextBox>().Class(":focus-within")), new Setter(TemplatedControl.BorderBrushProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<TextBox>().Template().OfType<TextBlock>().Name("PART_Watermark")), new Setter(TextBlock.ForegroundProperty, Brush(TextSecondary)));
        Add(new Style(x => x.OfType<ProgressBar>()), new(TemplatedControl.ForegroundProperty, Brush(Primary)),
            new(TemplatedControl.BackgroundProperty, Brush(Outline)), new(TemplatedControl.CornerRadiusProperty, new CornerRadius(ProgressRadius)));
    }
}
