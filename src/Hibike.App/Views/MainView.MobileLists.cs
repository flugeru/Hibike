using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly bool _mobileCollections = OperatingSystem.IsAndroid();
    private Grid? _mobileLibrary;
    private Grid? _mobileQueue;
    private Grid? _mobileSearchPage;
    private bool _queueOpenedFromPlayer;

    private bool IsMobileWorkspace => _mobileCollections && _page is "library" or "queue" or "explore";

    private void RefreshMobileCollections()
    {
        if (!IsMobileWorkspace)
        {
            if (_mobileLibrary is not null) _mobileLibrary.IsVisible = false;
            if (_mobileQueue is not null) _mobileQueue.IsVisible = false;
            if (_mobileSearchPage is not null) _mobileSearchPage.IsVisible = false;
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

        if (_page == "explore" && _mobileSearchPage is null)
        {
            _mobileSearchPage = BuildMobileSearch();
            Grid.SetRow(_mobileSearchPage, 1);
            Grid.SetRowSpan(_mobileSearchPage, content.RowDefinitions.Count - 1);
            content.Children.Insert(content.Children.IndexOf(_topMenuLayer), _mobileSearchPage);
        }

        // Search shares the main header. The collection screens own their back actions.
        content.Children[0].IsVisible = _page == "explore";
        content.Children[1].IsVisible = false;
        _statusBox.IsVisible = false;
        _searchLoading.IsVisible = false;
        _trackScroll.IsVisible = _listHeading.IsVisible = _listTools.IsVisible = _emptyState.IsVisible = false;
        _libraryGroupScroll.IsVisible = false;
        if (_mobileLibrary is not null) _mobileLibrary.IsVisible = _page == "library";
        if (_mobileQueue is not null) _mobileQueue.IsVisible = _page == "queue";
        if (_mobileSearchPage is not null) _mobileSearchPage.IsVisible = _page == "explore";
        if (_page == "library") RefreshMobileLibrary();
        else if (_page == "queue") RefreshMobileQueue();
        else RefreshMobileSearch();
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
