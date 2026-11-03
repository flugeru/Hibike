using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Control BuildMobilePlayerTools()
    {
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, ItemSpacing = 8 };
        buttons.Children.Add(ActionButton("Letra", ShowLyricsAsync));
        buttons.Children.Add(ActionButton("Fila", () => { ShowPlayerQueue(); return Task.CompletedTask; }));
        buttons.Children.Add(ActionButton("Temporizador", () => { ShowSleepTimer(); return Task.CompletedTask; }));
        return buttons;
    }

    private void ShowPlayerQueue()
    {
        var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 8 };
        var current = Text("", UiTheme.Body);
        current.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        layout.Children.Add(current);
        var radio = new StackPanel { Spacing = 4 };
        var radioState = Text("", UiTheme.Caption, Muted);
        radioState.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        radio.Children.Add(radioState);
        var retry = CreateRadioRetryButton();
        radio.Children.Add(retry);
        var toggle = ActionButton("", () => { _music.SetAutoplay(!_music.Autoplay); return Task.CompletedTask; });
        radio.Children.Add(toggle);
        Put(layout, radio, 1, 0);
        var tools = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        var undo = ActionButton("Desfazer", () => { _music.UndoQueueRemoval(); return Task.CompletedTask; });
        tools.Children.Add(undo);
        tools.Children.Add(ActionButton("Salvar próximas", () => { ShowSaveQueue(); return Task.CompletedTask; }));
        Put(layout, tools, 2, 0);

        Track[] displayed = [];
        var list = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : PlayerQueueRow(track))
        };
        Put(layout, UiTheme.FadingScroll(list), 3, 0);
        void Update()
        {
            current.Text = "Tocando agora · " + (_music.Current?.Title ?? "Sem faixa");
            radioState.Text = _music.RadioStatus;
            retry.Content = _music.RadioLoading ? "Buscando sugestões…" : "Buscar novas sugestões";
            retry.IsEnabled = _music.Current is not null && !_music.RadioLoading && !_music.LoadingAudio;
            toggle.Content = _music.Autoplay ? "Desligar rádio automática" : "Ligar rádio automática";
            undo.IsVisible = _music.CanUndoQueueRemoval;
            var upcoming = _music.Upcoming.ToArray();
            if (displayed.SequenceEqual(upcoming)) return;
            displayed = upcoming;
            list.ItemsSource = displayed;
        }
        Update();
        _music.Changed += Update;
        layout.DetachedFromVisualTree += (_, _) => _music.Changed -= Update;
        ShowSheet("Próximas músicas", layout, bottom: true);
        _overlay!.Tag = "player-queue";
    }

    private Control PlayerQueueRow(Track track)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Height = UiTheme.TouchSize + 8 };
        var label = ActionButton(track.Title, () => _music.PlayAsync(track, _music.Queue.IndexOf(track)));
        label.HorizontalContentAlignment = HorizontalAlignment.Left;
        var title = Text(track.Title, UiTheme.Body);
        title.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var artist = Text((_music.IsRecommended(track) ? "Rádio · " : "") + track.Artist, UiTheme.Caption, Muted);
        artist.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        label.Content = new StackPanel { Children = { title, artist } };
        row.Children.Add(label);
        var handle = new Border { Width = UiTheme.TouchSize, Background = Avalonia.Media.Brushes.Transparent,
            Child = Text("☰", UiTheme.Body, Muted) };
        handle.Child.HorizontalAlignment = HorizontalAlignment.Center;
        handle.Child.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(handle, "Arrastar " + track.Title);
        Point start = default;
        handle.PointerPressed += (_, e) =>
        {
            start = e.GetPosition(this);
            e.Pointer.Capture(handle);
            row.Opacity = .55;
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (e.Pointer.Captured != handle) return;
            var distance = (e.GetPosition(this).Y - start.Y) / row.Height;
            e.Pointer.Capture(null);
            row.Opacity = 1;
            _music.MoveUpcoming(track, (int)Math.Round(distance));
            e.Handled = true;
        };
        handle.PointerCaptureLost += (_, _) => row.Opacity = 1;
        Put(row, handle, 0, 1);
        Put(row, IconButton("close", "Remover " + track.Title, () => { _music.EditQueue(track, 0); return Task.CompletedTask; }), 0, 2);
        return row;
    }

    private void ShowSaveQueue()
    {
        var tracks = _music.Upcoming.ToArray();
        if (tracks.Length == 0) { _music.SetStatus("A fila está vazia."); return; }
        var field = UiTheme.Field("Nome da playlist");
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(field);
        panel.Children.Add(ActionButton("Criar playlist", () =>
        {
            _music.CreatePlaylist(field.Text ?? "", tracks);
            ShowPlayerQueue();
            return Task.CompletedTask;
        }));
        ShowSheet("Salvar próximas músicas", panel);
    }

    private void ShowSleepTimer()
    {
        var panel = new StackPanel { Spacing = 8 };
        var status = _music.SleepAfterTrack ? "Pausar ao fim desta faixa"
            : _music.SleepAt is { } deadline ? $"Pausar às {deadline.ToLocalTime():HH:mm}" : "Desativado";
        panel.Children.Add(Text(status, UiTheme.Body));
        foreach (var minutes in new[] { 15, 30, 45, 60, 90 })
            panel.Children.Add(ActionButton($"{minutes} minutos", () =>
            {
                _music.SleepAt = DateTime.UtcNow.AddMinutes(minutes);
                CloseSheet();
                return Task.CompletedTask;
            }));
        panel.Children.Add(ActionButton("Ao terminar a faixa", () => { _music.SetSleepAfterTrack(); CloseSheet(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Desativar", () => { _music.SleepAt = null; CloseSheet(); return Task.CompletedTask; }));
        ShowSheet("Temporizador", panel);
    }
}
