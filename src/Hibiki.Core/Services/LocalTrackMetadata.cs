using System.Buffers.Binary;
using System.Text;
using Hibiki.Core.Models;

namespace Hibiki.Core.Services;

public static class LocalTrackMetadata
{
    public static Track Read(Track track, string cacheDirectory)
    {
        if (!track.IsLocal) return track;
        try
        {
            using var file = File.OpenRead(track.Source);
            Span<byte> header = stackalloc byte[4]; file.ReadExactly(header);
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

    private static void Skip(BinaryReader reader, uint length)
    {
        if (length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
        reader.BaseStream.Seek(length, SeekOrigin.Current);
    }
}
