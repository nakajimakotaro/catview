using MangaViewer.Core.Imaging;

namespace MangaViewer.Core.Sources;

/// <summary>フォルダ内の画像ファイルをページとして扱う。サブフォルダは対象外。</summary>
public sealed class FolderPageSource : IPageSource
{
    private readonly string[] _files;
    private readonly PageSize[] _sizes;

    public FolderPageSource(string directory)
    {
        Path = System.IO.Path.GetFullPath(directory);
        _files = Directory.EnumerateFiles(Path)
            .Where(ImageFormats.IsImagePath)
            .Where(f => !System.IO.Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal))
            .OrderBy(f => System.IO.Path.GetFileName(f), NaturalSortComparer.Instance)
            .ToArray();
        _sizes = _files.Select(ReadSize).ToArray();
    }

    public string Path { get; }

    public int PageCount => _files.Length;

    public int IndexOf(string file)
    {
        var full = System.IO.Path.GetFullPath(file);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Array.FindIndex(_files, f => string.Equals(f, full, comparison));
    }

    public PageSize GetPageSize(int index) => _sizes[index];

    public string GetPageName(int index) => System.IO.Path.GetFileName(_files[index]);

    public Task<PageImage> GetPageAsync(int index, PageSize targetSize, CancellationToken ct)
    {
        var file = _files[index];
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var data = File.ReadAllBytes(file);
            return ImageDecoder.Decode(data, targetSize);
        }, ct);
    }

    public void Dispose()
    {
    }

    private static PageSize ReadSize(string file)
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
            return ImageHeaderReader.ReadSize(s);
        }
        catch (IOException)
        {
            return PageSize.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return PageSize.Empty;
        }
    }
}
