namespace Hibike.Core.Models;

// One snapshot of the playback route. Null means unreported, not unsupported.
// Output/Format describe samples at the OS or backend stage, not a device maximum.
// Device/Connection describe the routed listening endpoint, not the playback host.
public sealed record AudioOutputInfo(string? Device = null, string? Output = null,
    string? Connection = null, string? Format = null, string? Note = null);
