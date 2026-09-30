using MangaViewer.Core.Imaging;

namespace MangaViewer.Core.Sources;

public sealed record OpenedSource(IPageSource Source, int? InitialPage);

public static class PageSourceFactory
{
    public static readonly IReadOnlySet<string> ArchiveExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".cbz", ".pdf" };

    public static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ArchiveExtensions.Contains(ext) || ImageFormats.Extensions.Contains(ext);
    }

    /// <summary>
    /// パスに応じたページソースを開く。画像単体の場合は、その画像を含むフォルダ全体を対象とし、
    /// その画像のページを <see cref="OpenedSource.InitialPage"/> として返す。
    /// </summary>
    public static OpenedSource Open(string path)
    {
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full))
        {
            return new OpenedSource(new FolderPageSource(full), null);
        }
        if (!File.Exists(full))
        {
            throw new PageSourceException("ファイルが見つかりません");
        }

        var ext = Path.GetExtension(full);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenedSource(new PdfPageSource(full), null);
        }
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenedSource(new ZipPageSource(full), null);
        }
        if (ImageFormats.Extensions.Contains(ext))
        {
            var folder = new FolderPageSource(Path.GetDirectoryName(full)!);
            var index = folder.IndexOf(full);
            return new OpenedSource(folder, index >= 0 ? index : null);
        }
        throw new PageSourceException("対応していない形式です");
    }
}
