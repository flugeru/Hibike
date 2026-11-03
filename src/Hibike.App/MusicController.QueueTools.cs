using Hibike.Core.Models;

namespace Hibike.App;

public sealed partial class MusicController
{
    private (Track Track, int Index, int Session, bool Recommended)? _removedQueueItem;

    public bool CanUndoQueueRemoval => _removedQueueItem is { } item && item.Session == _queueSession;

    public void UndoQueueRemoval()
    {
        if (!CanUndoQueueRemoval || _removedQueueItem is not { } item) return;
        _removedQueueItem = null;
        if (Queue.Any(track => track.Source == item.Track.Source)) return;
        Queue.Insert(Math.Clamp(item.Index, CurrentQueueIndex + 1, Queue.Count), item.Track);
        if (item.Recommended) _recommended.Add(item.Track.Source);
        SetStatus("Faixa restaurada à fila.");
    }

    public void MoveUpcoming(Track track, int distance)
    {
        var index = Queue.IndexOf(track);
        var first = CurrentQueueIndex + 1;
        if (index < first || distance == 0) return;
        var target = Math.Clamp(index + distance, first, Queue.Count - 1);
        if (target == index) return;
        Queue.Move(index, target);
        WarmNext();
        SetStatus("Fila atualizada.");
    }
}
