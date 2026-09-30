namespace MangaViewer.Core.Imaging;

public static class ImageFormats
{
    /// <summary>対応画像形式（仕様 3.2）。</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".webp", ".gif", ".bmp",
    };

    public static bool IsImagePath(string path) => Extensions.Contains(Path.GetExtension(path));
}
