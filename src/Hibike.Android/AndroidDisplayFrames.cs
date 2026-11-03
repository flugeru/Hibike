using Android.Views;

namespace Hibike.Android;

// Used on the UI Looper, with at most one pending callback for home inertia.
internal sealed class AndroidDisplayFrames : Java.Lang.Object, Choreographer.IFrameCallback
{
    private Action<long>? _next;

    public void Post(Action<long> frame)
    {
        var needsCallback = _next is null;
        _next = frame;
        if (needsCallback) Choreographer.Instance!.PostFrameCallback(this);
    }

    public void DoFrame(long frameTimeNanos)
    {
        var frame = _next;
        _next = null;
        frame?.Invoke(frameTimeNanos);
    }
}
