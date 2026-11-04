using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private ScrollViewer? _preferencesPage;
    private void OpenPreferencesPage(string page, string title, string description, params Control[] sections)
    {
        ClosePlayer(); CloseTopMenu(); CloseSheet();
        var content = (Grid)_main.Child!;
        if (_preferencesPage is not null) content.Children.Remove(_preferencesPage);
        var body = new StackPanel { Spacing = 24, MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 24) };
        var back = CollectionAction("Voltar ao início", () => { _page = "home"; Refresh(); return Task.CompletedTask; });
        body.Children.Add(back);
        var summary = Text(description, UiTheme.Body, Muted); summary.TextWrapping = TextWrapping.Wrap; body.Children.Add(summary);
        foreach (var section in sections) body.Children.Add(section);
        _preferencesPage = UiTheme.FadingScroll(body);
        Put(content, _preferencesPage, 2, 0);
        _page = page; Refresh();
    }
    private static Control PreferenceSection(string title, string description, params Control[] controls)
    {
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        var heading = Text(title, UiTheme.Body); heading.FontWeight = FontWeight.SemiBold; body.Children.Add(heading);
        var note = Text(description, UiTheme.Caption, Muted); note.TextWrapping = TextWrapping.Wrap; body.Children.Add(note);
        foreach (var control in controls) body.Children.Add(control);
        return new Border { Background = UiTheme.Brush(UiTheme.Surface), BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = new Thickness(1), CornerRadius = new(16), Child = body };
    }
    private Control PreferenceLink(string icon, string title, string description, Func<Task> action)
    {
        var button = ActionButton("", action);
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Icons.Make(icon, Muted, 22));
        var labels = new StackPanel { Spacing = 4 };
        var label = Text(title, UiTheme.Body); label.TextWrapping = TextWrapping.Wrap; labels.Children.Add(label);
        var note = Text(description, UiTheme.Caption, Muted); note.TextWrapping = TextWrapping.Wrap; labels.Children.Add(note);
        Put(row, labels, 0, 1); Put(row, Icons.Make("forward", Muted, 18), 0, 2);
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(12);
        Avalonia.Automation.AutomationProperties.SetName(button, title); return button;
    }
}
