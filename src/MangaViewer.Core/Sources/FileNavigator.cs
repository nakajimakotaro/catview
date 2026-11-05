using MangaViewer.Core.Imaging;

namespace MangaViewer.Core.Sources;

/// <summary>
/// 同じフォルダ内の前後のファイルを探す（仕様 6.4）。
/// 対象は ZIP / CBZ / PDF と、画像を含むフォルダ（画像フォルダを開いている場合の兄弟フォルダ）。
/// </summary>
public static class FileNavigator
{
    /// <summary>
    /// 前後のファイルを返す。見つからなければ null。
    /// フォルダが画像を含むかの確認は、現在位置から指定方向へ順にたどりながら必要な分だけ行うため、
    /// サブフォルダが多い場所でも全フォルダの中身を調べることはない。
    /// </summary>
    public static string? FindSibling(string currentPath, int delta)
    {
        if (delta == 0) return null;

        string full;
        string? parent;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentPath));
            parent = Path.GetDirectoryName(full);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (parent is null || !Directory.Exists(parent)) return null;

        List<(string Path, bool IsDirectory)> entries;
        try
        {
            entries = Directory.EnumerateFiles(parent)
                .Where(f => PageSourceFactory.ArchiveExtensions.Contains(Path.GetExtension(f)))
                .Select(f => (f, false))
                .Concat(Directory.EnumerateDirectories(parent).Select(d => (d, true)))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!entries.Any(c => string.Equals(c.Path, full, comparison)))
        {
            entries.Add((full, Directory.Exists(full)));
        }
        entries.Sort((a, b) => NaturalSortComparer.Instance.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path)));
        var index = entries.FindIndex(c => string.Equals(c.Path, full, comparison));

        // 画像を含まないフォルダは候補にならないので飛ばし、|delta| 個目の候補を返す
        var step = Math.Sign(delta);
        var remaining = Math.Abs(delta);
        for (var i = index + step; i >= 0 && i < entries.Count; i += step)
        {
            var (path, isDirectory) = entries[i];
            if (isDirectory && !ContainsImages(path)) continue;
            if (--remaining == 0) return path;
        }
        return null;
    }

    private static bool ContainsImages(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Any(ImageFormats.IsImagePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
