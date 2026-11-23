using Hibike.Core.Models;
using Xunit;

public sealed class TrackQualityTests
{
    [Theory]
    [InlineData(" FLAC ", 16, 44100, "Sem perdas")]
    [InlineData("audio/alac", 24, 96000, "Alta resolução · sem perdas")]
    [InlineData("pcm_s24le", 24, 48000, "Sem perdas")]
    [InlineData("flac", null, 96000, "Sem perdas")]
    [InlineData("OPUS", 24, 96000, "Compressão com perdas")]
    [InlineData("mp4a.40.2", null, 44100, "Compressão com perdas")]
    [InlineData("audio/mp4a-latm", null, 48000, "Compressão com perdas")]
    [InlineData("audio/mpeg", null, 44100, "Compressão com perdas")]
    [InlineData("unknown", 24, 192000, "Qualidade não informada")]
    [InlineData(null, 24, 192000, "Qualidade não informada")]
    public void DescribesEncodingWithoutInferringQualityFromBitrateOrFilename(string? codec, int? bits, double hz, string expected)
    {
        var source = new AudioSource("track.flac", new Dictionary<string, string>(), BitrateKbps: 320,
            Codec: codec, SampleRateHz: hz, BitDepth: bits);
        Assert.Equal(expected, TrackQuality.Describe(source));
    }

    [Fact]
    public void NoSourceDoesNotClaimQuality() => Assert.Equal("Qualidade não informada", TrackQuality.Describe(null));
}
