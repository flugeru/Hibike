#if WINDOWS
using Hibike.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Hibike.Desktop;

internal sealed class WindowsWasapiEndpointProbe
{
    private sealed record Sample(AudioOutputInfo Route, AudioOutputInfo Result, long Started);
    private Sample? _sample;
    private int _busy;

    internal AudioOutputInfo Read(AudioOutputInfo route)
    {
        if (!string.Equals(route.Output, "wasapi", StringComparison.OrdinalIgnoreCase)) return route;
        var sample = Volatile.Read(ref _sample);
        var now = Environment.TickCount64;
        var age = ReferenceEquals(sample?.Route, route) ? now - sample!.Started : long.MaxValue;
        if (age >= 1500 && Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
            _ = Task.Run(() => Refresh(route, now));
        // Never show a device from a previous track or a panel last opened minutes ago.
        return age < 4000 ? sample!.Result : route;
    }

    private void Refresh(AudioOutputInfo route, long started)
    {
        try
        {
            var device = FindActiveEndpoint(Environment.ProcessId);
            Volatile.Write(ref _sample, new(route, device is null ? route : route with { Device = device }, started));
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    internal static string? FindActiveEndpoint(int processId)
    {
        // A session may survive a stop or a device switch. Its mere presence, or the
        // system default, is not evidence that this process is rendering on a device.
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? match = null;
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (endpoint)
                {
                    var sessions = endpoint.AudioSessionManager.Sessions;
                    var count = sessions.Count;
                    for (var i = 0; i < count; i++)
                    {
                        using var session = sessions[i];
                        if (session.State != AudioSessionState.AudioSessionStateActive || session.GetProcessID != processId) continue;
                        if (match is not null || !endpoint.Properties.TryGetValue<string>(PropertyKeys.PKEY_Device_FriendlyName, out match)
                            || string.IsNullOrWhiteSpace(match)) return null;
                        break; // Count matching endpoints, not matching sessions or distinct names.
                    }
                }
            }
            return match;
        }
        catch (Exception) { return null; } // Partial enumeration cannot establish uniqueness.
    }
}
#endif
