using MangaViewer.Core.Imaging;

namespace MangaViewer.Core.Sources;

/// <summary>
/// 同じフォルダ内の前後のファイルを探す（仕様 6.4）。
/// 対象は ZIP / CBZ / PDF と、画像を含むフォルダ（画像フォルダを開いている場合の兄弟フォルダ）。
/// </summary>
public static class FileNavigator
{
    public static string? FindSibling(string currentPath, int delta)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentPath));
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent)) return null;

        List<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(parent)
                .Where(f => PageSourceFactory.ArchiveExtensions.Contains(Path.GetExtension(f)))
                .Concat(Directory.EnumerateDirectories(parent).Where(ContainsImages))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidates.Any(c => string.Equals(c, full, comparison)))
        {
            candidates.Add(full);
        }
        candidates.Sort((a, b) => NaturalSortComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        var index = candidates.FindIndex(c => string.Equals(c, full, comparison));
        var next = index + delta;
        return next >= 0 && next < candidates.Count ? candidates[next] : null;
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
