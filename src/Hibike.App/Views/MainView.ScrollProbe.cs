using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private ScrollProbe? _scrollProbe;
    private string? _scrollReport;

    private void ObserveScrollGestures()
    {
        AddHandler(ScrollGestureInertiaStartingEvent, (_, e) =>
            _scrollProbe?.Start(e.Id, _showingPlayer ? "player" : _page), RoutingStrategies.Bubble, true);
        AddHandler(ScrollGestureEvent, (_, e) => _scrollProbe?.Step(e.Id), RoutingStrategies.Bubble, true);
        AddHandler(ScrollGestureEndedEvent, (_, e) => _scrollProbe?.End(e.Id), RoutingStrategies.Bubble, true);
        AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => _scrollProbe?.ScrollChanged(e), RoutingStrategies.Bubble, true);
    }

    private void AddScrollDiagnostics(StackPanel panel)
    {
        var start = ActionButton(_scrollProbe is null ? "Medir rolagem (20 s)" : "Medição em andamento…", () =>
        {
            var instructions = new StackPanel { Spacing = 12 };
            instructions.Children.Add(new TextBlock
            {
                Text = "Depois de iniciar, deslize e solte o dedo algumas vezes na tela que engasga. " +
                    "Após 20 segundos, volte às Preferências e toque em Copiar medição da rolagem.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            instructions.Children.Add(ActionButton("Iniciar medição", CaptureScrollAsync));
            ShowSheet("Medição da rolagem", instructions);
            return Task.CompletedTask;
        });
        start.IsEnabled = _scrollProbe is null;
        panel.Children.Add(start);
        if (_scrollProbe is null && _scrollReport is { } report)
            panel.Children.Add(ActionButton("Copiar medição da rolagem", async () =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(report);
            }));
    }

    private async Task CaptureScrollAsync()
    {
        if (_scrollProbe is not null) return;
        CloseSheet();
        _scrollReport = null;
        var probe = _scrollProbe = new ScrollProbe();
        using var timer = new Timer(_ => probe.SampleDispatcher(), null, 80, 80);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            _scrollReport = probe.Report();
        }
        finally { _scrollProbe = null; }
    }

    private sealed class ScrollProbe
    {
        private const int SampleLimit = 4096;
        private readonly List<Fling> _flings = [];
        private readonly Dictionary<int, Fling> _active = [];
        private readonly List<double> _dispatcher = [];
        private readonly int[] _initialCollections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        private int _sampleQueued;

        public void Start(int id, string page)
        {
            if (_flings.Count >= 40) return;
            var fling = new Fling(page) { LastStep = Stopwatch.GetTimestamp() };
            _flings.Add(fling);
            _active[id] = fling;
        }

        public void Step(int id)
        {
            if (!_active.TryGetValue(id, out var fling)) return;
            var now = Stopwatch.GetTimestamp();
            if (fling.Intervals.Count < SampleLimit)
                fling.Intervals.Add(Stopwatch.GetElapsedTime(fling.LastStep, now).TotalMilliseconds);
            fling.LastStep = now;
        }

        public void End(int id) => _active.Remove(id);

        public void Replace(int id)
        {
            if (_active.Remove(id, out var fling) && fling.Intervals.Count == 0)
                _flings.Remove(fling);
        }

        public void ScrollChanged(ScrollChangedEventArgs e)
        {
            foreach (var fling in _active.Values)
            {
                if (e.OffsetDelta != default) fling.OffsetUpdates++;
                if (e.ExtentDelta != default || e.ViewportDelta != default) fling.SizeChanges++;
            }
        }

        public void SampleDispatcher()
        {
            if (Interlocked.Exchange(ref _sampleQueued, 1) != 0) return;
            var started = Stopwatch.GetTimestamp();
            Dispatcher.UIThread.Post(() =>
            {
                if (_dispatcher.Count < SampleLimit)
                    _dispatcher.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Volatile.Write(ref _sampleQueued, 0);
            }, DispatcherPriority.Input);
        }

        public string Report()
        {
            var report = new StringBuilder("Hibike · Medição de rolagem (20 s)\n");
            report.AppendLine($"Fila da interface: {Describe(_dispatcher)}");
            report.AppendLine($"GC durante a medição (gerações 0/1/2): {string.Join("/", _initialCollections.Select((n, i) => GC.CollectionCount(i) - n))}");
            if (_flings.Count == 0) report.AppendLine("Nenhum gesto com inércia detectado.");
            foreach (var group in _flings.GroupBy(f => f.Page))
            {
                report.AppendLine($"{group.Key} · {group.Count()} gestos · {Describe(group.SelectMany(f => f.Intervals))}");
                report.AppendLine($"  Atualizações de posição: {group.Sum(f => f.OffsetUpdates)} · mudanças de tamanho: {group.Sum(f => f.SizeChanges)}");
            }
            return report.ToString();
        }

        private static string Describe(IEnumerable<double> samples)
        {
            var values = samples.Order().ToArray();
            if (values.Length == 0) return "sem amostras";
            return $"{values.Length} amostras, mediana {values[values.Length / 2]:0.0} ms, " +
                $"P95 {values[(int)((values.Length - 1) * .95)]:0.0} ms, máximo {values[^1]:0.0} ms, " +
                $"{values.Count(v => v >= 25)} ≥25 ms, {values.Count(v => v >= 50)} ≥50 ms";
        }

        private sealed class Fling(string page)
        {
            public string Page { get; } = page;
            public List<double> Intervals { get; } = [];
            public long LastStep;
            public int OffsetUpdates;
            public int SizeChanges;
        }
    }
}
