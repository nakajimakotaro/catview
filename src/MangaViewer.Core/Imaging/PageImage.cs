using SkiaSharp;

namespace MangaViewer.Core.Imaging;

/// <summary>
/// 参照カウント付きのページ画像。キャッシュと描画側がそれぞれ参照を持ち、最後の参照が解放されたときに破棄する。
/// </summary>
public sealed class PageImage
{
    private int _refCount = 1;

    private PageImage(SKImage image, PageSize originalSize, bool isFullResolution)
    {
        Image = image;
        OriginalSize = originalSize;
        IsFullResolution = isFullResolution;
        ByteSize = (long)image.Width * image.Height * 4;
    }

    public SKImage Image { get; }

    public int Width => Image.Width;

    public int Height => Image.Height;

    /// <summary>元画像のサイズ（縮小デコード前）。</summary>
    public PageSize OriginalSize { get; }

    /// <summary>これ以上の解像度で取得し直しても変わらない（原寸でデコード済み）か。</summary>
    public bool IsFullResolution { get; }

    public long ByteSize { get; }

    /// <summary>ビットマップの所有権を引き取って生成する。参照カウントは 1。</summary>
    public static PageImage FromBitmap(SKBitmap bitmap, PageSize originalSize, bool isFullResolution)
    {
        bitmap.SetImmutable();
        // 不変ビットマップからはピクセルをコピーせずに SKImage を作れる。ピクセルの寿命は SKImage が管理する
        var image = SKImage.FromBitmap(bitmap) ?? throw new InvalidOperationException("SKImage を作成できません");
        bitmap.Dispose();
        return new PageImage(image, originalSize, isFullResolution);
    }

    /// <summary>要求サイズを満たしているか。</summary>
    public bool Satisfies(PageSize target)
    {
        if (IsFullResolution || target.IsEmpty) return true;
        // わずかな差での再取得を避ける
        return Width >= target.Width * 0.9 && Height >= target.Height * 0.9;
    }

    public PageImage AddRef()
    {
        if (Interlocked.Increment(ref _refCount) <= 1)
        {
            throw new ObjectDisposedException(nameof(PageImage));
        }
        return this;
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref _refCount) == 0)
        {
            Image.Dispose();
        }
    }
}
