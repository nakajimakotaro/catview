using System.IO.Compression;
using MangaViewer.Core.Imaging;

namespace MangaViewer.Core.Sources;

/// <summary>ZIP（.zip / .cbz）内の画像ファイルをページとして扱う。全体を展開せず、必要なエントリのみ読む。</summary>
public sealed class ZipPageSource : IPageSource
{
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Lock _lock = new();
    private readonly ZipArchiveEntry[] _entries;
    private readonly string[] _names;
    private readonly PageSize[] _sizes;

    public ZipPageSource(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var raw = ZipCentralDirectory.Read(_stream);
            _stream.Seek(0, SeekOrigin.Begin);
            _archive = new ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: true);
            var archiveEntries = _archive.Entries;
            var sameOrder = raw.Count == archiveEntries.Count;

            var pages = new List<(ZipArchiveEntry Entry, string Name)>();
            for (var i = 0; i < archiveEntries.Count; i++)
            {
                var entry = archiveEntries[i];
                var name = sameOrder ? ZipCentralDirectory.DecodeName(raw[i]) : entry.FullName;
                name = name.Replace('\\', '/');
                if (!IsPageEntry(name)) continue;
                if (sameOrder && raw[i].IsEncrypted)
                {
                    throw new EncryptedFileException();
                }
                pages.Add((entry, name));
            }
            pages.Sort((a, b) => NaturalSortComparer.Instance.Compare(a.Name, b.Name));

            _entries = pages.Select(p => p.Entry).ToArray();
            _names = pages.Select(p => p.Name).ToArray();
            _sizes = _entries.Select(ReadSize).ToArray();
        }
        catch (InvalidDataException e)
        {
            _stream.Dispose();
            throw new PageSourceException("ZIP ファイルを読み込めません", e);
        }
        catch
        {
            _archive?.Dispose();
            _stream.Dispose();
            throw;
        }
    }

    public string Path { get; }

    public int PageCount => _entries.Length;

    public PageSize GetPageSize(int index) => _sizes[index];

    public string GetPageName(int index) => _names[index];

    public Task<PageImage> GetPageAsync(int index, PageSize targetSize, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            byte[] data;
            // ZipArchive はスレッドセーフではないため読み出しのみ直列化し、デコードは並列に行う
            lock (_lock)
            {
                var entry = _entries[index];
                using var s = entry.Open();
                data = new byte[entry.Length];
                s.ReadExactly(data);
            }
            return ImageDecoder.Decode(data, targetSize);
        }, ct);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }

    internal static bool IsPageEntry(string name)
    {
        if (name.EndsWith('/')) return false;
        var segments = name.Split('/');
        if (segments.Any(s => s.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))) return false;
        var fileName = segments[^1];
        if (fileName.StartsWith("._", StringComparison.Ordinal)) return false;
        // ZIP 内 ZIP やテキストなど、画像以外はすべて無視する
        return ImageFormats.IsImagePath(fileName);
    }

    private PageSize ReadSize(ZipArchiveEntry entry)
    {
        try
        {
            using var s = entry.Open();
            return ImageHeaderReader.ReadSize(s);
        }
        catch (InvalidDataException)
        {
            return PageSize.Empty;
        }
    }
}
