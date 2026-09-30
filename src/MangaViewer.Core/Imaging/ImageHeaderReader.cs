using System.Buffers.Binary;

namespace MangaViewer.Core.Imaging;

/// <summary>
/// 画像全体をデコードせず、ヘッダのみからピクセルサイズを取得する（仕様 5.4）。
/// ストリームはシーク不要（ZIP エントリの展開ストリームでも可）。
/// </summary>
public static class ImageHeaderReader
{
    public static PageSize ReadSize(Stream stream)
    {
        try
        {
            var head = new byte[32];
            var n = ReadAtMost(stream, head, head.Length);
            if (n < 10) return PageSize.Empty;
            var h = head.AsSpan(0, n);

            // PNG: IHDR が必ず先頭チャンク
            if (n >= 24 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            {
                return Make(BinaryPrimitives.ReadInt32BigEndian(h[16..]), BinaryPrimitives.ReadInt32BigEndian(h[20..]));
            }

            // GIF
            if (h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8')
            {
                return Make(BinaryPrimitives.ReadUInt16LittleEndian(h[6..]), BinaryPrimitives.ReadUInt16LittleEndian(h[8..]));
            }

            // BMP
            if (h[0] == 'B' && h[1] == 'M' && n >= 26)
            {
                var dibSize = BinaryPrimitives.ReadInt32LittleEndian(h[14..]);
                if (dibSize == 12)
                {
                    return Make(BinaryPrimitives.ReadUInt16LittleEndian(h[18..]), BinaryPrimitives.ReadUInt16LittleEndian(h[20..]));
                }
                return Make(BinaryPrimitives.ReadInt32LittleEndian(h[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h[22..])));
            }

            // WebP
            if (n >= 30 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
            {
                return ReadWebP(h);
            }

            // JPEG
            if (h[0] == 0xFF && h[1] == 0xD8)
            {
                return ReadJpeg(stream, head, n);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException)
        {
        }
        return PageSize.Empty;
    }

    private static PageSize ReadWebP(ReadOnlySpan<byte> h)
    {
        var chunk = h[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return Make(BinaryPrimitives.ReadUInt16LittleEndian(h[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(h[28..]) & 0x3FFF);
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            int b0 = h[21], b1 = h[22], b2 = h[23], b3 = h[24];
            var w = (b0 | ((b1 & 0x3F) << 8)) + 1;
            var hgt = ((b1 >> 6) | (b2 << 2) | ((b3 & 0x0F) << 10)) + 1;
            return Make(w, hgt);
        }
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var w = (h[24] | (h[25] << 8) | (h[26] << 16)) + 1;
            var hgt = (h[27] | (h[28] << 8) | (h[29] << 16)) + 1;
            return Make(w, hgt);
        }
        return PageSize.Empty;
    }

    private static PageSize ReadJpeg(Stream stream, byte[] head, int headLength)
    {
        // 先読み済みのバイト列とストリームを連結して読む
        var reader = new PrefixedReader(head, headLength, stream);
        reader.Skip(2); // SOI
        while (true)
        {
            var b = reader.ReadByte();
            if (b != 0xFF) continue;
            var marker = reader.ReadByte();
            while (marker == 0xFF) marker = reader.ReadByte();
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue; // 長さを持たないマーカー
            if (marker is 0xD9 or 0xDA) return PageSize.Empty; // EOI / SOS に到達
            var length = (reader.ReadByte() << 8) | reader.ReadByte();
            if (length < 2) return PageSize.Empty;
            var isSof = marker >= 0xC0 && marker <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof)
            {
                reader.Skip(1); // precision
                var height = (reader.ReadByte() << 8) | reader.ReadByte();
                var width = (reader.ReadByte() << 8) | reader.ReadByte();
                return Make(width, height);
            }
            reader.Skip(length - 2);
        }
    }

    private static PageSize Make(int w, int h) => w > 0 && h > 0 ? new PageSize(w, h) : PageSize.Empty;

    private static int ReadAtMost(Stream s, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var r = s.Read(buffer, total, count - total);
            if (r <= 0) break;
            total += r;
        }
        return total;
    }

    private sealed class PrefixedReader(byte[] prefix, int prefixLength, Stream stream)
    {
        private int _pos;
        private readonly byte[] _skipBuffer = new byte[4096];

        public int ReadByte()
        {
            if (_pos < prefixLength) return prefix[_pos++];
            var b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            return b;
        }

        public void Skip(int count)
        {
            var fromPrefix = Math.Min(count, prefixLength - _pos);
            if (fromPrefix > 0)
            {
                _pos += fromPrefix;
                count -= fromPrefix;
            }
            if (count <= 0) return;
            if (stream.CanSeek)
            {
                stream.Seek(count, SeekOrigin.Current);
                return;
            }
            while (count > 0)
            {
                var r = stream.Read(_skipBuffer, 0, Math.Min(count, _skipBuffer.Length));
                if (r <= 0) throw new EndOfStreamException();
                count -= r;
            }
        }
    }
}
