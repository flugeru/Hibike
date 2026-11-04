using System.Buffers.Binary;
using System.Text;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public static class LocalTrackMetadata
{
    public static AudioSource ReadAudioSource(AudioSource source)
    {
        try
        {
            using var file = File.OpenRead(source.Url);
            Span<byte> header = stackalloc byte[4]; file.ReadExactly(header);
            if (!header.SequenceEqual("fLaC"u8)) return source;
            ulong info = 0;
            Span<byte> block = stackalloc byte[34];
            bool last;
            do
            {
                file.ReadExactly(header); last = (header[0] & 128) != 0;
                var length = (header[1] << 16) | (header[2] << 8) | header[3];
                if (length > file.Length - file.Position) throw new InvalidDataException("Metadados FLAC truncados.");
                if ((header[0] & 127) == 0 && length == 34)
                {
                    file.ReadExactly(block);
                    info = BinaryPrimitives.ReadUInt64BigEndian(block.Slice(10, 8));
                }
                else file.Seek(length, SeekOrigin.Current);
            } while (!last);
            var rate = info >> 44; var samples = info & 0xFFFFFFFFFUL;
            if (rate == 0) return source;
            return source with
            {
                Codec = "flac", Container = "flac", SampleRateHz = rate,
                Channels = ((info >> 41) & 7) + 1, BitDepth = (int)((info >> 36) & 31) + 1,
                BitrateKbps = samples > 0 ? (file.Length - file.Position) * 8d * rate / samples / 1000 : source.BitrateKbps
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { PlaybackDiagnostics.Write("Áudio local", "Dados técnicos indisponíveis; dados existentes preservados."); return source; }
    }

    public static Track Read(Track track, string cacheDirectory)
    {
        if (!track.IsLocal) return track;
        try
        {
            using var file = File.OpenRead(track.Source);
            Span<byte> header = stackalloc byte[4]; file.ReadExactly(header);
            if (header[..3].SequenceEqual("ID3"u8)) return ReadId3(file, track, cacheDirectory, header[3]);
            if (!header.SequenceEqual("fLaC"u8)) return track;
            var result = track;
            bool last;
            do
            {
                file.ReadExactly(header); last = (header[0] & 128) != 0;
                var type = header[0] & 127;
                var length = (header[1] << 16) | (header[2] << 8) | header[3];
                if (length > file.Length - file.Position) throw new InvalidDataException("Metadados FLAC truncados.");
                if (type is not (0 or 4 or 6)) { file.Seek(length, SeekOrigin.Current); continue; }
                var bytes = new byte[length]; file.ReadExactly(bytes);
                if (type == 0 && length == 34)
                {
                    var info = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(10, 8));
                    var rate = info >> 44;
                    if (rate > 0) result = result with { DurationSeconds = (info & 0xFFFFFFFFFUL) / (double)rate };
                }
                else if (type == 4)
                {
                    using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
                    Skip(reader, reader.ReadUInt32());
                    var count = reader.ReadUInt32();
                    for (uint i = 0; i < count; i++)
                    {
                        var size = reader.ReadUInt32();
                        if (size > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
                        var tag = Encoding.UTF8.GetString(reader.ReadBytes((int)size));
                        var split = tag.IndexOf('='); if (split < 1) continue;
                        var value = tag[(split + 1)..].Trim(); if (value.Length == 0) continue;
                        if (tag[..split].Equals("TITLE", StringComparison.OrdinalIgnoreCase)) result = result with { Title = value };
                        if (tag[..split].Equals("ARTIST", StringComparison.OrdinalIgnoreCase)) result = result with { Artist = value };
                        if (tag[..split].Equals("ALBUM", StringComparison.OrdinalIgnoreCase)) result = result with { Album = value };
                        if (tag[..split].Equals("TRACKNUMBER", StringComparison.OrdinalIgnoreCase)) result = result with { TrackNumber = value };
                        if (tag[..split].Equals("DATE", StringComparison.OrdinalIgnoreCase)) result = result with { Date = value };
                    }
                }
                else if (type == 6)
                {
                    using var reader = new BinaryReader(new MemoryStream(bytes));
                    uint ReadBig() => BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    var pictureType = ReadBig();
                    var mimeLength = ReadBig();
                    if (mimeLength > 256) continue;
                    var mime = Encoding.ASCII.GetString(reader.ReadBytes((int)mimeLength));
                    Skip(reader, ReadBig()); Skip(reader, 16);
                    var size = ReadBig();
                    if (size == 0 || size > reader.BaseStream.Length - reader.BaseStream.Position || mime is not ("image/jpeg" or "image/png")) continue;
                    if (result.Thumbnail is not null && pictureType != 3) continue;
                    Directory.CreateDirectory(cacheDirectory);
                    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(track.Source))));
                    var path = Path.Combine(cacheDirectory, hash + (mime == "image/png" ? ".png" : ".jpg"));
                    var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllBytes(temp, reader.ReadBytes((int)size)); File.Move(temp, path, true); }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                    result = result with { Thumbnail = new Uri(path).AbsoluteUri };
                }
            } while (!last);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            PlaybackDiagnostics.Write("Metadados locais", ex.Message); return track;
        }
    }

    private static Track ReadId3(Stream file, Track track, string cacheDirectory, byte version)
    {
        if (version is not (3 or 4)) return track;
        Span<byte> tail = stackalloc byte[6]; file.ReadExactly(tail);
        if ((tail[1] & 0xC0) != 0) return track; // Extended headers/unsynchronization need a format-aware decoder.
        static int Synchsafe(ReadOnlySpan<byte> b) => (b[0] << 21) | (b[1] << 14) | (b[2] << 7) | b[3];
        if (tail[2..].ContainsAnyInRange((byte)128, (byte)255)) return track;
        var length = Synchsafe(tail[2..]);
        if (length > 16 * 1024 * 1024 || length > file.Length - file.Position) return track;
        var end = file.Position + length;
        Span<byte> frame = stackalloc byte[10];
        while (file.Position + 10 <= end)
        {
            file.ReadExactly(frame);
            if (frame[0] == 0) break;
            var size = version == 4 ? Synchsafe(frame[4..8]) : BinaryPrimitives.ReadInt32BigEndian(frame[4..8]);
            if (size <= 0 || size > end - file.Position) break;
            if (frame[9] != 0) { file.Seek(size, SeekOrigin.Current); continue; }
            var bytes = new byte[size]; file.ReadExactly(bytes);
            var id = Encoding.ASCII.GetString(frame[..4]);
            if (id == "APIC")
            {
                var mimeEnd = Array.IndexOf(bytes, (byte)0, 1);
                if (mimeEnd < 0 || mimeEnd + 2 >= bytes.Length) continue;
                var mime = Encoding.ASCII.GetString(bytes, 1, mimeEnd - 1);
                var start = mimeEnd + 2;
                var wide = bytes[0] is 1 or 2;
                while (start + (wide ? 1 : 0) < bytes.Length && !(bytes[start] == 0 && (!wide || bytes[start + 1] == 0))) start += wide ? 2 : 1;
                start += wide ? 2 : 1;
                if (start >= bytes.Length || mime is not ("image/jpeg" or "image/png") || bytes[mimeEnd + 1] != 3 && track.Thumbnail is not null) continue;
                Directory.CreateDirectory(cacheDirectory);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(track.Source))));
                var path = Path.Combine(cacheDirectory, hash + (mime == "image/png" ? ".png" : ".jpg"));
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temp, bytes[start..]); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                track = track with { Thumbnail = new Uri(path).AbsoluteUri };
                continue;
            }
            if (id[0] != 'T' || bytes.Length < 2) continue;
            var encoding = bytes[0] switch { 0 => Encoding.Latin1, 1 => bytes.Length > 2 && bytes[1] == 0xFE ? Encoding.BigEndianUnicode : Encoding.Unicode, 2 => Encoding.BigEndianUnicode, 3 => Encoding.UTF8, _ => null };
            if (encoding is null) continue;
            var value = encoding.GetString(bytes, 1, bytes.Length - 1).Trim('\0', '\uFEFF', ' ');
            if (value.Length == 0) continue;
            track = id switch
            {
                "TIT2" => track with { Title = value }, "TPE1" => track with { Artist = value.Replace('\0', ';') },
                "TALB" => track with { Album = value }, "TRCK" => track with { TrackNumber = value },
                "TYER" or "TDRC" => track with { Date = value }, _ => track
            };
        }
        return track;
    }

    private static void Skip(BinaryReader reader, uint length)
    {
        if (length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
        reader.BaseStream.Seek(length, SeekOrigin.Current);
    }
}
