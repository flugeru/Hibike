using System.Text;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;

public sealed class LocalMetadataTests
{
    [Fact]
    public void ReadsFlacTechnicalDataWithoutCountingMetadataInBitrate()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var file = File.Create(path))
            {
                file.Write("fLaC"u8); file.Write([128, 0, 0, 34]);
                var block = new byte[34];
                var info = (48000UL << 44) | (1UL << 41) | (23UL << 36) | 96000UL;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(block.AsSpan(10, 8), info);
                file.Write(block); file.Write(new byte[2000]);
            }
            var source = new AudioSource(path, new Dictionary<string, string>());
            var actual = LocalTrackMetadata.ReadAudioSource(source);
            Assert.Equal("flac", actual.Codec); Assert.Equal("flac", actual.Container);
            Assert.Equal(48000d, actual.SampleRateHz); Assert.Equal(2d, actual.Channels);
            Assert.Equal(24, actual.BitDepth); Assert.Equal(8d, actual.BitrateKbps);
            File.WriteAllBytes(path, "fLaC"u8.ToArray());
            Assert.Equal(source, LocalTrackMetadata.ReadAudioSource(source));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadsId3TextFromFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var file = File.Create(path))
            {
                var text = Encoding.UTF8.GetBytes("\u0003Título real");
                var length = text.Length + 10;
                file.Write([73, 68, 51, 3, 0, 0, 0, 0, 0, (byte)length]);
                file.Write("TIT2"u8);
                file.Write([0, 0, 0, (byte)text.Length, 0, 0]);
                file.Write(text);
            }
            var track = LocalTrackMetadata.Read(new("local", "filename", "unknown", path, IsLocal: true), Path.GetTempPath());
            Assert.Equal("Título real", track.Title);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IdentifiesChannelWithoutMistakingVideoForArtist()
    {
        Assert.True(new Track("artist", "Artist", "Artist", "https://www.youtube.com/channel/UCkksVnsKY9FF0X6JHsFdehA").IsArtist);
        Assert.False(new Track("song", "Song", "Artist", "https://www.youtube.com/watch?v=abc").IsArtist);
        Assert.False(new Track("fake", "Fake", "Artist", "https://youtube.com.evil.test/channel/abc").IsArtist);
    }
}
