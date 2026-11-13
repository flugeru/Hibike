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
            Grid.SetRow(_mobileLibrary, 2);
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
                Grid.SetRow(_mobileQueue, 2);
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
