using System.Reflection;
using Hibike.Desktop;
using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static class WasapiEndpointChecks
{
    internal static async Task Run()
    {
        var method = typeof(MpvAudioPlayer).Assembly.GetType("Hibike.Desktop.WindowsWasapiEndpointProbe")!
            .GetMethod("FindActiveEndpoint", BindingFlags.Static | BindingFlags.NonPublic)!;
        var find = method.CreateDelegate<Func<int, string?>>();
        async Task Expect(string? name)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (await Task.Run(() => find(Environment.ProcessId)) == name) return;
                await Task.Delay(100);
            }
            throw new Exception($"WASAPI session endpoint mismatch; expected '{name ?? "unreported"}'.");
        }

        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        await Expect(null);
        if (endpoints.Count == 0) { Console.WriteLine("WASAPI: no render endpoints; active-session check skipped."); return; }
        static WasapiOut PlaySilence(MMDevice endpoint)
        {
            var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, false, 100);
            try
            {
                output.Init(new BufferedWaveProvider(new WaveFormat(48000, 16, 2)) { ReadFully = true });
                output.Play();
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        using var first = endpoints[0];
        using var playback = PlaySilence(first);
        await Expect(first.FriendlyName);
        if (await Task.Run(() => find(-1)) is not null) throw new Exception("WASAPI returned another process's endpoint.");
        if (endpoints.Count > 1)
        {
            using var other = endpoints[1];
            using var concurrent = PlaySilence(other);
            await Expect(null);
            Console.WriteLine("WASAPI: concurrent endpoints suppressed.");
        }
        await Expect(first.FriendlyName);
        playback.Stop();
        await Expect(null);
        Console.WriteLine("WASAPI: active, inactive and foreign process checks passed.");
    }
}
