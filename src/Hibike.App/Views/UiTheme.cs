using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Hibike.App.Views;

internal static class UiTheme
{
    public const string Background = "#000000", Surface = "#0D0D0D", SurfaceElevated = "#191919",
        Outline = "#2B2B2B", Primary = "#F2F2F2", PrimaryContainer = "#242424", OnPrimary = "#080808",
        Success = "#71D6A3", Warning = "#FFCC80", Error = "#FF8A8A", TextPrimary = "#F5F5F5",
        TextSecondary = "#B0B0B0", TextDisabled = "#686868", Scrim = "#CC000000";
    public const double CardRadius = 18, ControlRadius = 12, ThumbRadius = 8, RoundRadius = 22,
        MobileGutter = 10, CompactGap = 8, SectionGap = 14, PageGap = 18, CardGap = 16, CardPadding = 20,
        TopBarTitle = 18, PageTitle = 28, SectionTitle = 18, DialogTitle = 22, Body = 15, Caption = 13, Label = 12,
        ButtonText = 14, TouchSize = 48, IconSize = 24, IconStroke = 1.8, RoundSize = 44,
        ThumbnailSize = 52, PlaySize = 72, TransportIconSize = 32,
        ProgressHeight = 8, ProgressRadius = 4, MiniProgressHeight = 4, SeekHeight = 6,
        SeekThumbSize = 16, SeekThumbPressedSize = 20, PageMaxWidth = 1280, PlayerMaxWidth = 440,
        DialogMaxWidth = 560, CompactWidth = 390, DesktopWidth = 800, SidebarWidth = 104,
        NavHeight = 72, RowHeight = 76, TrackRowHeight = 70, MarkerSize = 7, EqualizerGap = 3, EqualizerBarWidth = 4, EqualizerStep = 5,
        PauseStroke = 4, StateTransitionMilliseconds = 160, EqualizerMilliseconds = 180,
        ListFadeHeight = 18,
        DesktopPlayerGap = 48, ToggleIconHeight = 36,
        DesktopArtworkMax = 500, DesktopPlayerMaxWidth = 1120, DesktopPlaySize = 56,
        MarqueeFrameMilliseconds = 30, MarqueeStep = .8, MarqueePauseFrames = 60, TitleLineHeight = 24;
    public static readonly Thickness PageMargin = new(14, 14, 14, 18), ButtonPadding = new(12, 7),
        RowPadding = new(4, 8), ChipPadding = new(10, 4), BannerPadding = new(14, 10),
        One = new(1), Zero = new(0), InnerPadding = new(12), DialogMargin = new(14),
        MiniMargin = new(14, 0, 14, 8), TopBorder = new(0, 1, 0, 0), BottomBorder = new(0, 0, 0, 1),
        RoundInset = new(2), MiniPlayInset = new(4);
    public static readonly Thickness SearchIconPadding = new(16, 0, 12, 0);
    public static readonly Thickness MobileContentPadding = new(MobileGutter, 2, MobileGutter, 4),
        MobileMiniMargin = new(MobileGutter, 0, MobileGutter, CompactGap),
        MobilePlayerMargin = new(MobileGutter, CompactGap, MobileGutter, CompactGap);
    public static readonly Thickness ContentPadding = new(14, 14, 14, 8);
    public static readonly Thickness DesktopPlayerMargin = new(32, 20, 32, 24), NavPadding = new(12, 8);
    public static readonly Thickness DesktopTransportPadding = new(32, 16, 32, 20);
    public static ScrollViewer FadingScroll(Control? content = null, bool horizontal = false)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden
        };
        var android = OperatingSystem.IsAndroid();
        (double Fade, byte Start, byte End)? previous = null;
        void UpdateMask()
        {
            var length = horizontal ? scroll.Viewport.Width : scroll.Viewport.Height;
            var extent = horizontal ? scroll.Extent.Width : scroll.Extent.Height;
            var offset = horizontal ? scroll.Offset.X : scroll.Offset.Y;
            var remaining = Math.Max(0, extent - length - offset);
            if (length <= 0 || extent <= length + 1) { scroll.OpacityMask = null; previous = null; return; }
            var fade = Math.Min(.5, (android ? 12 : horizontal ? 20 : 16) / length);
            var strength = android ? 144 : 191;
            var start = (byte)(255 - strength * Math.Clamp(offset / 12, 0, 1));
            var end = (byte)(255 - strength * Math.Clamp(remaining / 12, 0, 1));
            if (previous == (fade, start, end)) return;
            previous = (fade, start, end);
            // Fade only hidden content; keep first/last items intact at scroll limits.
            scroll.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new(0, 0, RelativeUnit.Relative),
                EndPoint = horizontal ? new(1, 0, RelativeUnit.Relative) : new(0, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new(Color.FromArgb(start, 0, 0, 0), 0),
                    new(Colors.Black, fade), new(Colors.Black, 1 - fade),
                    new(Color.FromArgb(end, 0, 0, 0), 1)
                ]
            };
        }
        scroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty || e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty) UpdateMask();
        };
        scroll.SizeChanged += (_, _) => UpdateMask();
        return scroll;
    }
    public static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();
    public static TextBlock Text(string? text, double size = Body, string color = TextPrimary,
        FontWeight weight = FontWeight.Normal) => new()
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
    public static Border AudioBadge(Control child) => new()
    {
        Child = child, Padding = new Thickness(16, 8), CornerRadius = new(24),
        BorderBrush = Brush("#353535"), BorderThickness = One,
        Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
            GradientStops = [new(Color.Parse("#202323"), 0), new(Color.Parse("#090A0A"), .6), new(Color.Parse("#151717"), 1)]
        },
        VerticalAlignment = VerticalAlignment.Center
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
    public static Button OutlinedButton(string text) => new()
    {
        Content = text, HorizontalAlignment = HorizontalAlignment.Stretch,
        RenderTransformOrigin = RelativePoint.Center,
        Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(120), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }
        }
    };
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
            new(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
            new(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Add(new Style(x => x.OfType<Button>().Class("primary")), new(TemplatedControl.BackgroundProperty, Brush(Primary)),
            new(TemplatedControl.ForegroundProperty, Brush(OnPrimary)), new(TemplatedControl.BorderThicknessProperty, Zero),
            new(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(new Style(x => x.OfType<Button>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new Setter(ContentPresenter.ForegroundProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<Button>().Class("primary").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new Setter(ContentPresenter.ForegroundProperty, Brush(OnPrimary)));
        Add(new Style(x => x.OfType<Button>().Class("subtle")), new Setter(TemplatedControl.BorderThicknessProperty, Zero));
        Add(new Style(x => x.OfType<Button>().Class("cover")), new(TemplatedControl.BorderThicknessProperty, Zero),
            new(TemplatedControl.CornerRadiusProperty, new CornerRadius(CardRadius)), new(TemplatedControl.PaddingProperty, new Thickness(8)));
        Add(new Style(x => x.OfType<Button>().Class("pill")), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(24)));
        Add(new Style(x => x.OfType<Button>().Class(":focus-visible").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new(ContentPresenter.BorderBrushProperty, Brush(Primary)), new(ContentPresenter.BorderThicknessProperty, new Thickness(2)));
        Add(new Style(x => x.OfType<Button>().Class("round")), new(Layoutable.WidthProperty, TouchSize),
            new(Layoutable.HeightProperty, TouchSize), new(TemplatedControl.PaddingProperty, Zero),
            new(TemplatedControl.CornerRadiusProperty, new CornerRadius(RoundRadius)));
        Add(new Style(x => x.OfType<Button>().Class("round").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new Setter(Layoutable.MarginProperty, RoundInset));
        Add(new Style(x => x.OfType<Button>().Class("miniPlay").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new Setter(Layoutable.MarginProperty, MiniPlayInset));
        Add(new Style(x => x.OfType<Button>().Class("largePlay").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new Setter(Layoutable.MarginProperty, Zero));
        Add(new Style(x => x.OfType<Button>().Class("nav")), new(TemplatedControl.BorderThicknessProperty, Zero), new(TemplatedControl.PaddingProperty, Zero));
        Add(new Style(x => x.OfType<Button>().Class(":disabled")), new Setter(Visual.OpacityProperty, .4));
        Add(new Style(x => x.OfType<Button>()),
            new Setter(Visual.RenderTransformProperty, Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)")));
        Add(new Style(x => x.OfType<Button>().Class(":pressed")),
            new Setter(Visual.RenderTransformProperty, Avalonia.Media.Transformation.TransformOperations.Parse("scale(0.97)")));
        // Target Fluent's presenter so local backgrounds do not suppress pointer states.
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            Add(new Style(x => x.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
                new(ContentPresenter.BackgroundProperty, Brush(SurfaceElevated)), new(ContentPresenter.BorderBrushProperty, Brush(Primary)),
                new(ContentPresenter.ForegroundProperty, Brush(Primary)));
            Add(new Style(x => x.OfType<Button>().Class("primary").Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
                new(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse(Primary), state == ":pressed" ? .75 : .9)),
                new(ContentPresenter.ForegroundProperty, Brush(OnPrimary)));
        }
        Add(new Style(x => x.OfType<Button>().Class("cardAction")),
            new(Layoutable.WidthProperty, TouchSize), new(Layoutable.HeightProperty, TouchSize),
            new(Layoutable.MinWidthProperty, TouchSize), new(Layoutable.MinHeightProperty, TouchSize),
            new(Layoutable.MaxWidthProperty, TouchSize), new(Layoutable.MaxHeightProperty, TouchSize),
            new(TemplatedControl.PaddingProperty, Zero), new(TemplatedControl.BorderThicknessProperty, Zero),
            new(TemplatedControl.CornerRadiusProperty, new CornerRadius(22)),
            new(TemplatedControl.BackgroundProperty, Brush(Scrim)),
            new(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
            new(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Add(new Style(x => x.OfType<Button>().Class("topMenuAction")),
            new(TemplatedControl.BorderThicknessProperty, Zero), new(TemplatedControl.PaddingProperty, new Thickness(12, 8)),
            new(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        foreach (var state in new[] { ":pointerover", ":pressed" })
            Add(new Style(x => x.OfType<Button>().Class("topMenuAction").Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
                new(ContentPresenter.BackgroundProperty, Brushes.Transparent), new(ContentPresenter.BorderThicknessProperty, Zero));
        Add(new Style(x => x.OfType<Button>().Class("cardAction").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
            new(Layoutable.MarginProperty, RoundInset), new(TemplatedControl.PaddingProperty, Zero),
            new(TemplatedControl.BorderThicknessProperty, Zero), new(TemplatedControl.CornerRadiusProperty, new CornerRadius(22)));
        Add(new Style(x => x.OfType<TextBox>()), new(TemplatedControl.BackgroundProperty, Brush(Surface)),
            new(TemplatedControl.ForegroundProperty, Brush(TextPrimary)), new(TemplatedControl.BorderBrushProperty, Brush(Outline)),
            new(TemplatedControl.BorderThicknessProperty, One), new(TemplatedControl.CornerRadiusProperty, new CornerRadius(ControlRadius)),
            new(TemplatedControl.PaddingProperty, ButtonPadding), new(TemplatedControl.FontSizeProperty, Body), new(Layoutable.MinHeightProperty, TouchSize));
        Add(new Style(x => x.OfType<TextBox>().Class(":focus-within")), new Setter(TemplatedControl.BorderBrushProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<TextBox>().Template().OfType<TextBlock>().Name("PART_Watermark")), new Setter(TextBlock.ForegroundProperty, Brush(TextSecondary)));
        Add(new Style(x => x.OfType<ProgressBar>()), new(TemplatedControl.ForegroundProperty, Brush(Primary)),
            new(TemplatedControl.BackgroundProperty, Brush(Outline)), new(TemplatedControl.CornerRadiusProperty, new CornerRadius(ProgressRadius)));
        Add(new Style(x => x.OfType<Slider>().Template().OfType<Thumb>()),
            new(Layoutable.WidthProperty, SeekThumbSize), new(Layoutable.HeightProperty, SeekThumbSize),
            new(TemplatedControl.BackgroundProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<Slider>().Class(":pressed").Template().OfType<Thumb>()),
            new(Layoutable.WidthProperty, SeekThumbPressedSize), new(Layoutable.HeightProperty, SeekThumbPressedSize),
            new(TemplatedControl.BackgroundProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<Slider>().Template().OfType<RepeatButton>().Name("PART_DecreaseButton")),
            new Setter(TemplatedControl.BackgroundProperty, Brush(Primary)));
        Add(new Style(x => x.OfType<Slider>().Template().OfType<RepeatButton>().Name("PART_IncreaseButton")),
            new Setter(TemplatedControl.BackgroundProperty, Brush(Outline)));
    }
    public static void InstallResources(IResourceDictionary resources)
    {
        resources["SliderTrackValueFill"] = Brush(Primary);
        resources["SliderTrackFill"] = Brush(Outline);
        resources["SliderTrackThemeHeight"] = SeekHeight;
        resources["SliderHorizontalThumbWidth"] = SeekThumbSize;
        resources["SliderHorizontalThumbHeight"] = SeekThumbSize;
        resources["SliderThumbBackground"] = Brush(Primary);
        resources["SliderThumbBackgroundPointerOver"] = Brush(Primary);
        resources["SliderThumbBackgroundPressed"] = Brush(Primary);
        resources["SliderTrackValueFillPointerOver"] = Brush(Primary);
        resources["SliderTrackValueFillPressed"] = Brush(Primary);
        resources["SliderTrackFillPointerOver"] = Brush(Outline);
        resources["SliderTrackFillPressed"] = Brush(Outline);
        resources["ControlCornerRadius"] = new CornerRadius(ControlRadius);
        resources["SliderThumbCornerRadius"] = new CornerRadius(SeekThumbPressedSize / 2);
        resources["TextControlBorderBrushFocused"] = Brush(Primary);
        resources["TextControlBorderBrushPointerOver"] = Brush(Primary);
        resources["TextControlBackgroundFocused"] = Brush(Surface);
        resources["TextControlBackgroundPointerOver"] = Brush(Surface);
        resources["TextControlPlaceholderForeground"] = Brush(TextSecondary);
    }
}
