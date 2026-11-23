using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private void ShowAudioInfo()
    {
        var panel = new StackPanel { Spacing = UiTheme.SectionGap };
        var title = AudioText("", UiTheme.SectionTitle, UiTheme.TextPrimary, FontWeight.SemiBold);
        panel.Children.Add(title);

        var path = new StackPanel { Spacing = UiTheme.CompactGap };
        panel.Children.Add(path);
        path.Children.Add(AudioText("Caminho do sinal", UiTheme.Body, UiTheme.TextPrimary, FontWeight.SemiBold));
        var sourceStage = Stage("01", "Fonte");
        var outputStage = Stage("02", "Saída de áudio");
        var deviceStage = Stage("03", "Dispositivo de escuta");
        var routeNote = AudioText("", UiTheme.Caption, Muted);
        path.Children.Add(routeNote);

        (TextBlock Value, TextBlock Detail) Stage(string number, string label)
        {
            var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
            var marker = UiTheme.Chip(UiTheme.Text(number, UiTheme.Label, UiTheme.Accent, FontWeight.SemiBold));
            marker.Background = UiTheme.Brush(UiTheme.AccentContainer);
            marker.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(marker);
            var labels = new StackPanel { Spacing = 2 };
            labels.Children.Add(AudioText(label, UiTheme.Label, Muted));
            var value = AudioText("", UiTheme.Body, UiTheme.TextPrimary, FontWeight.SemiBold);
            var detail = AudioText("", UiTheme.Caption, Muted);
            detail.IsVisible = false;
            labels.Children.Add(value); labels.Children.Add(detail);
            Put(grid, labels, 0, 1);
            path.Children.Add(UiTheme.Inner(grid));
            return (value, detail);
        }

        var volumeGroup = new StackPanel();
        var volumeLabel = AudioText("", UiTheme.Body, Muted);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = _music.Volume, MinHeight = UiTheme.TouchSize, SmallChange = .05, LargeChange = .1 };
        AutomationProperties.SetName(volume, "Volume");
        volume.ValueChanged += (_, _) => { _music.Volume = volume.Value; volumeLabel.Text = $"Volume · {volume.Value:P0}"; };
        volumeGroup.Children.Add(volumeLabel); volumeGroup.Children.Add(volume); panel.Children.Add(volumeGroup);

        panel.Children.Add(AudioText("Detalhes da faixa", UiTheme.Body, UiTheme.TextPrimary, FontWeight.SemiBold));
        var details = new StackPanel { Spacing = UiTheme.CompactGap };
        panel.Children.Add(details);
        var note = AudioText("", UiTheme.Caption, Muted);
        panel.Children.Add(note);
        var mediaStatus = AudioText("", UiTheme.Caption, Muted);
        panel.Children.Add(mediaStatus);
        var extra = new StackPanel { Spacing = UiTheme.CompactGap };
        panel.Children.Add(extra);
        if (DeveloperFeatures.Enabled) panel.Children.Add(ActionButton("Abrir diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        (Track? Track, AudioSource? Source, bool Pending)? last = null;
        var revision = 0;
        SelectableTextBlock? durationValue = null;
        bool Visible() => panel.IsEffectivelyVisible && _overlay?.IsVisualAncestorOf(panel) == true;
        void RefreshTrack()
        {
            if (!Visible()) return;
            var pending = _music.Pending is not null;
            var track = _music.Pending ?? _music.Current;
            // The controller keeps the previous stream until the pending track is ready.
            var source = pending || track is null ? null : _music.CurrentSource;
            var duration = Math.Max(!pending && source is not null ? _music.Player.Duration : 0, track?.DurationSeconds ?? 0);
            var adjustable = _compactPlayer && _showingPlayer;
            volume.IsVisible = adjustable;
            volume.Value = _music.Volume;
            volumeLabel.Text = $"Volume · {_music.Volume:P0}";
            if (last != (track, source, pending))
            {
                last = (track, source, pending);
                var currentRevision = ++revision;
                title.Text = track is null ? "Nenhuma faixa selecionada" : TrackTextFormatter.Title(source?.Title ?? track.Title, source?.Artist ?? track.Artist);
                sourceStage.Value.Text = pending ? "Preparando áudio…" : TrackQuality.Describe(source);
                var rate = source?.SampleRateHz is > 0 ? $"{source.SampleRateHz / 1000:0.###} kHz" : null;
                var depth = source?.BitDepth is > 0 ? $"{source.BitDepth} bits" : null;
                var bitrate = source?.BitrateKbps is > 0 ? $"{source.BitrateKbps:0.#} kb/s" : null;
                var origin = track is null ? null : track.IsLocal ? "Arquivo local" : "Online";
                SetOptional(sourceStage.Detail, string.Join(" · ", new[] { origin, source?.Codec?.ToUpperInvariant(), rate, depth, bitrate }.Where(s => !string.IsNullOrWhiteSpace(s))));

                details.Children.Clear(); extra.Children.Clear();
                AudioDetail(details, "Artista", string.IsNullOrWhiteSpace(source?.Artist) ? track?.Artist : source.Artist);
                AudioDetail(details, "Origem", origin);
                durationValue = AudioDetail(details, "Duração", "—");
                AudioDetail(details, "Álbum", track?.Album ?? source?.Album);
                AudioDetail(details, "Faixa", track?.TrackNumber);
                AudioDetail(details, "Data", track?.Date);
                AudioDetail(details, track?.IsLocal == true ? "Bitrate médio do áudio" : "Bitrate da fonte", bitrate);
                AudioDetail(details, "Codec", source?.Codec);
                AudioDetail(details, "Contêiner", source?.Container);
                AudioDetail(details, "Amostragem", rate);
                AudioDetail(details, "Canais", source?.Channels?.ToString("0"));
                AudioDetail(details, "Profundidade", depth);
                if (track?.IsLocal != true) AudioDetail(details, "Formato", source?.Format);
                SetOptional(note, track is null ? null : track.IsLocal
                    ? "Dados lidos do arquivo. Em FLAC, o bitrate médio é calculado pelo tamanho do áudio e pelo número de amostras, sem incluir os metadados."
                    : "Dados fornecidos pela fonte. O bitrate pode variar; valores ausentes não são estimados.");
                if (!pending && track?.IsLocal == true && (!OperatingSystem.IsAndroid() || source?.Codec is null) && AppServices.ReadMediaInfo is { } read)
                {
                    SetOptional(mediaStatus, "Lendo metadados do arquivo…");
                    _ = ReadExtraAsync(track, currentRevision, read);
                }
                else SetOptional(mediaStatus, track is null || pending ? null : track.IsLocal ? "Sem leitura adicional nesta plataforma."
                    : "Dados do fluxo selecionado e do catálogo. MediaInfo não recebe URLs autenticadas nem cookies.");
            }
            if (durationValue?.Parent is Grid durationRow)
            {
                durationRow.IsVisible = duration > 0;
                durationValue.Text = Time(duration);
            }
        }

        async Task ReadExtraAsync(Track track, int request, Func<string, Task<IReadOnlyDictionary<string, string>>> read)
        {
            bool Current() => request == revision && Visible() && _music.Pending is null && _music.Current == track;
            try
            {
                var data = await read(track.Source);
                if (!Current()) return;
                mediaStatus.Text = data.Count == 0 ? "Leitor indisponível ou arquivo não reconhecido. Metadados existentes preservados."
                    : OperatingSystem.IsAndroid() ? "Detalhes adicionais · Android" : "Detalhes adicionais · MediaInfo";
                foreach (var pair in data) AudioDetail(extra, pair.Key, pair.Value);
            }
            catch (Exception) { if (Current()) mediaStatus.Text = "Leitura adicional indisponível. Metadados existentes preservados."; }
        }

        void RefreshRoute()
        {
            if (!Visible()) return;
            var route = _music.AudioOutput;
            outputStage.Value.Text = string.IsNullOrWhiteSpace(route.Output) ? "Saída não informada" : route.Output;
            SetOptional(outputStage.Detail, string.IsNullOrWhiteSpace(route.Format) ? "Formato de saída não informado" : $"Formato de saída: {route.Format}");
            deviceStage.Value.Text = string.IsNullOrWhiteSpace(route.Device) ? "Dispositivo não informado" : route.Device;
            SetOptional(deviceStage.Detail, string.IsNullOrWhiteSpace(route.Connection) ? null : $"Conexão: {route.Connection}");
            var explanation = route.Note;
            if (explanation is null && string.IsNullOrWhiteSpace(route.Device) && string.IsNullOrWhiteSpace(route.Output))
                explanation = _music.Player.IsPlaying ? "O sistema ainda não informou a saída em uso."
                    : "Inicie ou retome a reprodução para identificar a saída em uso.";
            SetOptional(routeNote, explanation);
        }

        void Poll(object? sender, EventArgs args) { RefreshTrack(); RefreshRoute(); }
        panel.AttachedToVisualTree += (_, _) =>
        {
            _music.Changed += RefreshTrack;
            timer.Tick += Poll;
            Poll(null, EventArgs.Empty);
            timer.Start();
        };
        panel.DetachedFromVisualTree += (_, _) =>
        {
            timer.Stop(); timer.Tick -= Poll;
            _music.Changed -= RefreshTrack;
            revision++; last = null;
        };
        ShowSheet("Informações do áudio", panel, preservePlayer: true);
    }

    private static TextBlock AudioText(string text, double size, string color, FontWeight weight = FontWeight.Normal)
    {
        var label = UiTheme.Text(text, size, color, weight);
        label.TextWrapping = TextWrapping.Wrap;
        return label;
    }

    private static void SetOptional(TextBlock label, string? text)
    {
        label.Text = text;
        label.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private static SelectableTextBlock? AudioDetail(Panel parent, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var row = new Grid { ColumnDefinitions = new("2*,3*"), ColumnSpacing = UiTheme.CompactGap };
        row.Children.Add(AudioText(label, UiTheme.Caption, Muted));
        var text = new SelectableTextBlock { Text = value, FontFamily = UiTheme.Inter, FontSize = UiTheme.Body,
            Foreground = UiTheme.Brush(UiTheme.TextPrimary), TextWrapping = TextWrapping.Wrap };
        Put(row, text, 0, 1); parent.Children.Add(row);
        return text;
    }
}
