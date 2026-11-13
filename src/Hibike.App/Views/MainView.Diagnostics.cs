using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private static readonly (DiagnosticSector Sector, string Label)[] DiagnosticSections =
    [
        (DiagnosticSector.Playback, "Reprodução"),
        (DiagnosticSector.Discord, "Conta Discord"),
        (DiagnosticSector.Google, "Conta Google"),
        (DiagnosticSector.Catalog, "Busca, catálogo e rádio"),
        (DiagnosticSector.Storage, "Arquivos e sessão"),
        (DiagnosticSector.App, "Aplicativo")
    ];

    private void ShowDiagnostics(DiagnosticSector initial = DiagnosticSector.Playback)
    {
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = UiTheme.CompactGap };
        var filters = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var section = new ComboBox
        {
            ItemsSource = DiagnosticSections.Select(item => item.Label).ToArray(),
            SelectedIndex = Math.Max(0, Array.FindIndex(DiagnosticSections, item => item.Sector == initial)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(section, "Setor do diagnóstico");
        filters.Children.Add(section);
        layout.Children.Add(filters);

        var report = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = UiTheme.Mono,
            FontSize = UiTheme.Label,
            Foreground = UiTheme.Brush(UiTheme.TextPrimary),
            Margin = new Thickness(UiTheme.CompactGap)
        };
        var scroll = UiTheme.FadingScroll(report);
        scroll.Height = Math.Clamp(Bounds.Height * .38, 100, 360);
        Put(layout, UiTheme.Card(scroll, UiTheme.CompactGap), 1, 0);

        var feedback = Text("", UiTheme.Caption, Muted);
        feedback.TextWrapping = TextWrapping.Wrap;
        Put(layout, feedback, 2, 0);
        var actions = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = UiTheme.CompactGap };
        actions.Children.Add(ActionButton("Copiar setor", () => CopyAsync(all: false)));
        actions.Children[0].Classes.Add("primary");
        Put(actions, ActionButton("Copiar todos", () => CopyAsync(all: true)), 0, 1);
        Put(layout, actions, 3, 0);

        Put(filters, CollectionIcon("refresh", "Atualizar relatório", () =>
        {
            UpdateReport();
            return Task.CompletedTask;
        }), 0, 1);

        section.SelectionChanged += (_, _) => UpdateReport();
        UpdateReport();
        ShowSheet("Diagnósticos", layout, preservePlayer: true, scrollContent: false);

        (DiagnosticSector Sector, string Label) Selected() => DiagnosticSections[Math.Max(0, section.SelectedIndex)];

        void UpdateReport()
        {
            report.Text = FormatDiagnosticSection(Selected(), PlaybackDiagnostics.Capture());
            scroll.Offset = default;
            feedback.Text = "";
        }

        async Task CopyAsync(bool all)
        {
            var snapshot = PlaybackDiagnostics.Capture();
            var selected = Selected();
            report.Text = FormatDiagnosticSection(selected, snapshot);
            var platform = OperatingSystem.IsAndroid() ? "Android" : OperatingSystem.IsWindows() ? "Windows"
                : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : ".NET";
            var version = typeof(MainView).Assembly.GetName().Version?.ToString(3);
            var sections = all ? DiagnosticSections : [selected];
            var body = string.Join(Environment.NewLine + Environment.NewLine, sections.Select(item => FormatDiagnosticSection(item, snapshot)));
            var text = $"Hibike {version} · {platform}{Environment.NewLine}{Environment.NewLine}{body}";
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null) throw new InvalidOperationException();
                await clipboard.SetTextAsync(text);
                feedback.Text = all ? "Todos os setores copiados." : "Setor copiado.";
            }
            catch
            {
                feedback.Text = "Não foi possível copiar. Você pode selecionar o texto do relatório.";
            }
        }
    }

    private static string FormatDiagnosticSection((DiagnosticSector Sector, string Label) section, IReadOnlyList<DiagnosticEntry> entries)
    {
        var lines = entries.Where(entry => entry.Sector == section.Sector).ToArray();
        var detail = lines.Length == 0 ? "Nenhum registro nesta execução." : string.Join(Environment.NewLine, lines.Select(entry => entry.ToString()));
        return section.Label + Environment.NewLine + Environment.NewLine + detail;
    }
}
