using System.Buffers.Binary;
using System.Text;

namespace MangaViewer.Core.Sources;

/// <summary>
/// ZIP のセントラルディレクトリを直接読み、各エントリの生のファイル名バイト列と汎用ビットフラグを取得する。
/// System.IO.Compression はアーカイブ単位でしか文字コードを指定できず、フラグも公開していないため。
/// エントリの並びは ZipArchive.Entries と同じ順序になる。
/// </summary>
internal static class ZipCentralDirectory
{
    public readonly record struct Entry(ushort Flags, byte[] RawName)
    {
        public bool IsUtf8 => (Flags & 0x0800) != 0;

        public bool IsEncrypted => (Flags & 0x0001) != 0;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// ファイル名を決定する（仕様 3.3）。UTF-8 フラグが立っていれば UTF-8、
    /// そうでなければ UTF-8 の厳密デコードを試み、失敗したら Shift_JIS とする。
    /// </summary>
    public static string DecodeName(Entry entry)
    {
        if (entry.IsUtf8)
        {
            return Encoding.UTF8.GetString(entry.RawName);
        }
        try
        {
            return StrictUtf8.GetString(entry.RawName);
        }
        catch (DecoderFallbackException)
        {
            return ShiftJis.GetString(entry.RawName);
        }
    }

    // CodePagesEncodingProvider の登録が必要（Program 起動時に行う）
    private static Encoding ShiftJis => Encoding.GetEncoding(932);

    public static IReadOnlyList<Entry> Read(Stream stream)
    {
        var (count, offset) = FindCentralDirectory(stream);
        var entries = new List<Entry>((int)Math.Min(count, 65536));
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        for (long i = 0; i < count; i++)
        {
            if (reader.ReadUInt32() != 0x02014B50)
            {
                throw new InvalidDataException("ZIP のセントラルディレクトリが壊れています");
            }
            stream.Seek(4, SeekOrigin.Current); // version made by, version needed
            var flags = reader.ReadUInt16();
            stream.Seek(18, SeekOrigin.Current); // method, time, date, crc, sizes
            var nameLength = reader.ReadUInt16();
            var extraLength = reader.ReadUInt16();
            var commentLength = reader.ReadUInt16();
            stream.Seek(12, SeekOrigin.Current); // disk, attrs, local header offset
            var name = reader.ReadBytes(nameLength);
            stream.Seek(extraLength + commentLength, SeekOrigin.Current);
            entries.Add(new Entry(flags, name));
        }
        return entries;
    }

    private static (long Count, long Offset) FindCentralDirectory(Stream stream)
    {
        const int eocdSize = 22;
        var length = stream.Length;
        if (length < eocdSize) throw new InvalidDataException("ZIP ファイルではありません");

        var tailLength = (int)Math.Min(length, eocdSize + ushort.MaxValue);
        var tail = new byte[tailLength];
        stream.Seek(length - tailLength, SeekOrigin.Begin);
        stream.ReadExactly(tail);

        for (var p = tailLength - eocdSize; p >= 0; p--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p)) != 0x06054B50) continue;

            long count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 10));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p + 16));
            if (count == 0xFFFF || offset == 0xFFFFFFFF)
            {
                // ZIP64: EOCD の直前にロケータがある
                var locatorPos = length - tailLength + p - 20;
                if (locatorPos >= 0)
                {
                    var locator = new byte[20];
                    stream.Seek(locatorPos, SeekOrigin.Begin);
                    stream.ReadExactly(locator);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(locator) == 0x07064B50)
                    {
                        var eocd64Pos = (long)BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
                        var eocd64 = new byte[56];
                        stream.Seek(eocd64Pos, SeekOrigin.Begin);
                        stream.ReadExactly(eocd64);
                        if (BinaryPrimitives.ReadUInt32LittleEndian(eocd64) == 0x06064B50)
                        {
                            count = (long)BinaryPrimitives.ReadUInt64LittleEndian(eocd64.AsSpan(32));
                            offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(eocd64.AsSpan(48));
                        }
                    }
                }
            }
            return (count, offset);
        }
        throw new InvalidDataException("ZIP ファイルではありません");
    }
}
