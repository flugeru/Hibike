using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly bool _mobileCollections = OperatingSystem.IsAndroid();
    private const string CollectionAccent = "#CEBEFF";
    private Grid? _mobileLibrary;
    private Grid? _mobileQueue;
    private bool _queueOpenedFromPlayer;

    private bool IsMobileCollection => _mobileCollections && _page is "library" or "queue";

    private void RefreshMobileCollections()
    {
        if (!IsMobileCollection)
        {
            if (_mobileLibrary is not null) _mobileLibrary.IsVisible = false;
            if (_mobileQueue is not null) _mobileQueue.IsVisible = false;
            return;
        }

        var content = (Grid)_main.Child!;
        if (_page == "library" && _mobileLibrary is null)
        {
            _mobileLibrary = BuildMobileLibrary();
            Grid.SetRowSpan(_mobileLibrary, content.RowDefinitions.Count);
            content.Children.Add(_mobileLibrary);
        }
        if (_page == "queue" && _mobileQueue is null)
        {
            _mobileQueue = BuildMobileQueue();
            Grid.SetRowSpan(_mobileQueue, content.RowDefinitions.Count);
            content.Children.Add(_mobileQueue);
        }

        // The compact pages own their headers and scrolling viewport.
        content.Children[0].IsVisible = false;
        content.Children[1].IsVisible = false;
        _statusBox.IsVisible = false;
        _searchLoading.IsVisible = false;
        _trackScroll.IsVisible = _listHeading.IsVisible = _listTools.IsVisible = _emptyState.IsVisible = false;
        _libraryGroupScroll.IsVisible = false;
        if (_mobileLibrary is not null) _mobileLibrary.IsVisible = _page == "library";
        if (_mobileQueue is not null) _mobileQueue.IsVisible = _page == "queue";
        if (_page == "library") RefreshMobileLibrary();
        else RefreshMobileQueue();
    }

    private static TextBlock CollectionText(string? value, double size, string color = UiTheme.TextPrimary,
        FontWeight weight = FontWeight.Normal)
    {
        var text = UiTheme.Text(value, size, color, weight);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        return text;
    }

    private static ItemsControl CollectionList<T>(Func<T, Control> create)
    {
        return new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<T>((item, _) => item is null ? new Border() : create(item))
        };
    }

    private Button CollectionIcon(string icon, string label, Func<Task> execute, string color = UiTheme.TextPrimary)
    {
        var button = IconButton(icon, label, execute, color);
        button.Classes.Add("subtle");
        return button;
    }

    private Button CollectionAction(string label, Func<Task> execute, bool prominent = false)
    {
        var button = ActionButton(label, execute);
        button.Classes.Add(prominent ? "primary" : "subtle");
        button.CornerRadius = new(24);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    private static Border CollectionRule() => new() { Height = 1, Background = UiTheme.Brush(UiTheme.Outline) };

    private Control CollectionEmpty(string icon, string title, string description, Button? action = null)
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(24, 8), MaxWidth = 330,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new Border
        {
            Width = 52, Height = 52, CornerRadius = new(16), Background = UiTheme.Brush(UiTheme.SurfaceElevated),
            HorizontalAlignment = HorizontalAlignment.Center, Child = Icons.Make(icon, CollectionAccent, 22)
        });
        foreach (var text in new[] { CollectionText(title, UiTheme.SectionTitle, weight: FontWeight.SemiBold),
            CollectionText(description, UiTheme.Caption, Muted) })
        {
            text.TextWrapping = TextWrapping.Wrap;
            text.TextAlignment = TextAlignment.Center;
            stack.Children.Add(text);
        }
        if (action is not null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(action);
        }
        return stack;
    }

    private Control CollectionTrackRow(Track track, Func<Task> playTrack, Action<Control> openMenu,
        string? caption = null, bool recommended = false, bool highlightCurrent = false)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Height = 64 };
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var title = CollectionText(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body, weight: FontWeight.Medium);
        labels.Children.Add(title);
        var artist = TrackTextFormatter.Artist(track.Artist);
        labels.Children.Add(CollectionText(caption ?? artist, UiTheme.Caption, recommended ? CollectionAccent : Muted));

        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        metadata.Children.Add(new Artwork(track, 46) { VerticalAlignment = VerticalAlignment.Center });
        Put(metadata, labels, 0, 1);
        var play = ActionButton("", playTrack);
        play.Classes.Add("subtle");
        play.Padding = UiTheme.Zero;
        play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        play.Content = metadata;
        AutomationProperties.SetName(play, $"{track.Title}, {artist}");
        row.Children.Add(play);

        var more = CollectionIcon("more", $"Opções de {track.Title}", () => Task.CompletedTask, Muted);
        more.Click += (_, _) => openMenu(more);
        Put(row, more, 0, 1);
        var container = new Border { Child = row, BorderBrush = UiTheme.Brush(UiTheme.SurfaceElevated), BorderThickness = UiTheme.BottomBorder };
        if (highlightCurrent)
        {
            var active = UiTheme.Brush(CollectionAccent);
            var inactive = title.Foreground;
            void Update() => title.Foreground = (_music.Pending ?? _music.Current)?.Source == track.Source ? active : inactive;
            container.AttachedToVisualTree += (_, _) => { _music.Changed += Update; Update(); };
            container.DetachedFromVisualTree += (_, _) => _music.Changed -= Update;
        }
        return container;
    }
}
