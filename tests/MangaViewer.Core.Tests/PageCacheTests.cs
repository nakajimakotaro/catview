using MangaViewer.Core.Caching;
using MangaViewer.Core.Imaging;
using MangaViewer.Core.Sources;
using SkiaSharp;

namespace MangaViewer.Core.Tests;

public class PageCacheTests
{
    private sealed class FakeSource(int count) : IPageSource
    {
        public int Loads;

        public string Path => "fake";

        public int PageCount => count;

        public PageSize GetPageSize(int index) => new(100, 100);

        public string GetPageName(int index) => index.ToString();

        public Task<PageImage> GetPageAsync(int index, PageSize targetSize, CancellationToken ct)
        {
            Interlocked.Increment(ref Loads);
            var bmp = new SKBitmap(100, 100); // 40,000 bytes
            return Task.FromResult(PageImage.FromBitmap(bmp, new PageSize(100, 100), true));
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task 同じページは再読み込みしない()
    {
        var source = new FakeSource(10);
        using var cache = new PageCache(source, 1_000_000);
        (await cache.GetAsync(1, PageSize.Empty, CancellationToken.None))!.Release();
        (await cache.GetAsync(1, PageSize.Empty, CancellationToken.None))!.Release();
        Assert.Equal(1, source.Loads);
    }

    [Fact]
    public async Task 上限を超えたら現在位置から遠いページを破棄する()
    {
        var source = new FakeSource(10);
        using var cache = new PageCache(source, 40_000 * 3);
        cache.SetPosition(5);
        foreach (var p in new[] { 5, 6, 0, 4 })
        {
            (await cache.GetAsync(p, PageSize.Empty, CancellationToken.None))?.Release();
        }
        Assert.False(cache.Contains(0));
        Assert.True(cache.Contains(4) && cache.Contains(5) && cache.Contains(6));
    }

    [Fact]
    public async Task 破棄後も参照中の画像は使える()
    {
        var source = new FakeSource(10);
        var cache = new PageCache(source, 1_000_000);
        var img = await cache.GetAsync(0, PageSize.Empty, CancellationToken.None);
        cache.Dispose();
        Assert.Equal(100, img!.Image.Width);
        img.Release();
    }

    [Fact]
    public async Task キャンセル済みなら読み込みを始めない()
    {
        var source = new FakeSource(10);
        using var cache = new PageCache(source, 1_000_000);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(0, PageSize.Empty, new CancellationToken(true)));
        Assert.Equal(0, source.Loads);
    }
}
