using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;
namespace Hibike.Core.Tests;
public sealed class PlaybackSessionStoreTests
{
 [Fact] public void RejectsQueueOutsideLimits()
 {
  var d=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
  try { var t=new Track("a","A","B","https://example.test/a"); File.WriteAllText(Path.Combine(d,"playback-session.json"),JsonSerializer.Serialize(new PlaybackSession(Enumerable.Repeat(t,PlaybackSessionStore.MaxQueue+1).ToArray(),null,-1,0))); Assert.Throws<InvalidDataException>(()=>new PlaybackSessionStore(d).Load()); } finally { Directory.Delete(d,true); }
 }
}
