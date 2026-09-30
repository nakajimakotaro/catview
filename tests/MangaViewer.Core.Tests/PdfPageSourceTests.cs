using MangaViewer.Core.Sources;
using SkiaSharp;

namespace MangaViewer.Core.Tests;

public sealed class PdfPageSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mv-{Guid.NewGuid()}.pdf");

    public PdfPageSourceTests()
    {
        using var stream = File.Create(_path);
        using var doc = SKDocument.CreatePdf(stream);
        // 縦長 2 ページと横長 1 ページ（単位はポイント）
        foreach (var (w, h) in new[] { (360f, 540f), (360f, 540f), (720f, 540f) })
        {
            using var canvas = doc.BeginPage(w, h);
            canvas.Clear(SKColors.White);
            doc.EndPage();
        }
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void ページ数とサイズを取得できる()
    {
        using var source = new PdfPageSource(_path);
        Assert.Equal(3, source.PageCount);
        Assert.Equal(new PageSize(480, 720), source.GetPageSize(0));
        Assert.True(source.GetPageSize(2).IsLandscape);
    }

    [Fact]
    public async Task 指定サイズでレンダリングする()
    {
        using var source = new PdfPageSource(_path);
        var img = await source.GetPageAsync(0, new PageSize(200, 300), CancellationToken.None);
        Assert.Equal((200, 300), (img.Width, img.Height));
        Assert.False(img.IsFullResolution);
        img.Release();
    }

    [Fact]
    public void PDFでないファイル()
    {
        File.WriteAllText(_path, "not a pdf");
        Assert.Throws<PageSourceException>(() => new PdfPageSource(_path));
    }
}
