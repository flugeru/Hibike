using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private ScrollViewer? _preferencesPage;
    private Button _preferencesBack = null!;
    private bool _accountBackToSettings;

    private void BackFromPreferences()
    {
        if (_page == "account" && _accountBackToSettings)
        {
            _accountBackToSettings = false;
            ShowSettings();
            return;
        }
        _page = "home";
        Refresh();
    }
    private void OpenPreferencesPage(string page, params Control[] sections)
    {
        ClosePlayer(); CloseTopMenu(); CloseSheet();
        var content = (Grid)_main.Child!;
        if (_preferencesPage is not null) content.Children.Remove(_preferencesPage);
        var body = new StackPanel { Spacing = UiTheme.SectionGap, MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, UiTheme.CompactGap, 0, UiTheme.PageGap) };
        foreach (var section in sections) body.Children.Add(section);
        _preferencesPage = UiTheme.FadingScroll(body);
        Put(content, _preferencesPage, 2, 0);
        _page = page; Refresh();
    }
    private static Control PreferenceSection(string title, params Control[] controls)
    {
        var body = new StackPanel { Spacing = 4 };
        var heading = CollectionText(title, UiTheme.Caption, Muted, FontWeight.SemiBold);
        heading.Margin = new Thickness(0, 0, 0, 4);
        body.Children.Add(heading);
        foreach (var control in controls) body.Children.Add(control);
        return UiTheme.Card(body, UiTheme.ControlRadius);
    }
    private Control PreferenceLink(string icon, string title, string description, Func<Task> action)
    {
        var button = ActionButton("", action);
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Icons.Make(icon, Muted, 22));
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var label = Text(title, UiTheme.Body); label.TextWrapping = TextWrapping.Wrap; labels.Children.Add(label);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var note = Text(description, UiTheme.Caption, Muted);
            note.TextWrapping = TextWrapping.Wrap;
            labels.Children.Add(note);
        }
        Put(row, labels, 0, 1); Put(row, Icons.Make("forward", Muted, 18), 0, 2);
        button.Classes.Add("subtle");
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(0, UiTheme.CompactGap);
        Avalonia.Automation.AutomationProperties.SetName(button, title); return button;
    }
}
