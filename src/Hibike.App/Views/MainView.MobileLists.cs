using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly bool _androidLayout;
    private Grid? _mobileLibrary;
    private Grid? _mobileQueue;
    private Grid? _mobileSearchPage;
    private bool _queueOpenedFromPlayer;

    private bool IsCollectionPage => _page is "library" or "queue" or "explore";

    private void RefreshCollectionPages()
    {
        var content = _pageContent;
        content.Children[0].IsVisible = _page is not ("library" or "queue");

        if (_page == "library" && _mobileLibrary is null)
        {
            _mobileLibrary = BuildMobileLibrary();
            PlaceOwnHeaderPage(_mobileLibrary);
            content.Children.Add(_mobileLibrary);
        }

        if (_page == "explore" && _mobileSearchPage is null)
        {
            _mobileSearchPage = BuildMobileSearch();
            Grid.SetRow(_mobileSearchPage, 1);
            Grid.SetRowSpan(_mobileSearchPage, content.RowDefinitions.Count - 1);
            content.Children.Insert(content.Children.IndexOf(_topMenuLayer), _mobileSearchPage);
        }

        var queueOnSide = _queuePane.IsVisible;
        if (_page == "queue" || queueOnSide)
        {
            _mobileQueue ??= BuildMobileQueue();
            if (queueOnSide && _queuePane.Child != _mobileQueue)
            {
                content.Children.Remove(_mobileQueue);
                _queuePane.Child = _mobileQueue;
            }
            else if (!queueOnSide && !content.Children.Contains(_mobileQueue))
            {
                _queuePane.Child = null;
                PlaceOwnHeaderPage(_mobileQueue);
                content.Children.Add(_mobileQueue);
            }
            _mobileQueue.IsVisible = true;
            RefreshMobileQueue();
        }
        else if (_mobileQueue is not null) _mobileQueue.IsVisible = false;

        if (_mobileLibrary is not null) _mobileLibrary.IsVisible = _page == "library";
        if (_mobileSearchPage is not null) _mobileSearchPage.IsVisible = _page == "explore";
        if (_page == "library") RefreshMobileLibrary();
        else if (_page == "explore") RefreshMobileSearch();
    }

    private static TextBlock TopBarTitle(string text) => CollectionText(text, UiTheme.TopBarTitle, weight: FontWeight.SemiBold);

    // Pages that draw their own app bar start where the shared header would, instead of two
    // collapsed rows (and their spacing) lower, so every title sits at the same height.
    private void PlaceOwnHeaderPage(Control page)
    {
        Grid.SetRow(page, _androidLayout ? 0 : 2);
        Grid.SetRowSpan(page, _androidLayout ? _pageContent.RowDefinitions.Count : 1);
    }

    private Grid AppBar(Button? leading, TextBlock title, Control? trailing)
    {
        // Gaps are margins on the actions, not column spacing, so a hidden back button leaves the
        // title at the same left edge as every other page.
        var bar = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        if (_androidLayout) bar.Height = UiTheme.TopBarHeight;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        if (leading is not null) { leading.Margin = new Thickness(0, 0, UiTheme.CompactGap, 0); bar.Children.Add(leading); }
        if (trailing is not null) trailing.Margin = new Thickness(UiTheme.CompactGap, 0, 0, 0);
        Put(bar, title, 0, 1);
        if (trailing is not null) { trailing.VerticalAlignment = VerticalAlignment.Center; Put(bar, trailing, 0, 2); }
        return bar;
    }

    private static Border CollectionSegments(Grid tabs)
    {
        var frame = UiTheme.Card(tabs, padding: 3);
        frame.CornerRadius = new CornerRadius(UiTheme.ControlRadius);
        return frame;
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
        if (prominent) button.Classes.Add("primary");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

}
