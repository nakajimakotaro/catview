using SkiaSharp;

namespace MangaViewer.Core.Imaging;

public static class ImageDecoder
{
    /// <summary>
    /// 画像をデコードする。JPEG など縮小デコードに対応する形式では、targetSize を下回らない範囲で縮小してデコードする。
    /// GIF は先頭フレームのみ。
    /// </summary>
    public static PageImage Decode(byte[] data, PageSize targetSize)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData) ?? throw new InvalidDataException("画像をデコードできません");

        var full = codec.Info;
        var dims = full.Size;
        var isFull = true;
        if (!targetSize.IsEmpty && full.Width > 0 && full.Height > 0)
        {
            var scale = Math.Max((float)targetSize.Width / full.Width, (float)targetSize.Height / full.Height);
            if (scale < 1f)
            {
                var scaled = codec.GetScaledDimensions(scale);
                if (scaled.Width >= targetSize.Width && scaled.Height >= targetSize.Height && scaled.Width < full.Width)
                {
                    dims = scaled;
                    isFull = false;
                }
            }
        }

        var info = new SKImageInfo(dims.Width, dims.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        try
        {
            var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(0));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                throw new InvalidDataException($"画像をデコードできません ({result})");
            }
            return PageImage.FromBitmap(bitmap, new PageSize(full.Width, full.Height), isFull);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
