using MangaViewer.Core;
using MangaViewer.Core.Imaging;
using SkiaSharp;

namespace MangaViewer.Core.Tests;

public class ImageHeaderReaderTests
{
    public static byte[] Encode(int w, int h, SKEncodedImageFormat format, int quality = 90)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(SKColors.Coral);
        using var data = bmp.Encode(format, quality);
        return data.ToArray();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, 0)]
    [InlineData(SKEncodedImageFormat.Jpeg, 0)]
    [InlineData(SKEncodedImageFormat.Webp, 90)]
    [InlineData(SKEncodedImageFormat.Webp, 100)] // 100 はロスレス（VP8L）
    public void エンコードした画像のサイズを読める(SKEncodedImageFormat format, int quality)
    {
        var bytes = Encode(123, 45, format, quality == 0 ? 90 : quality);
        Assert.Equal(new PageSize(123, 45), ImageHeaderReader.ReadSize(new NonSeekableStream(bytes)));
    }

    [Fact]
    public void Gif()
    {
        byte[] gif = [.. "GIF89a"u8, 0x2C, 0x01, 0x90, 0x01, 0, 0, 0, 0];
        Assert.Equal(new PageSize(300, 400), ImageHeaderReader.ReadSize(new MemoryStream(gif)));
    }

    [Fact]
    public void Bmp_トップダウン()
    {
        var bmp = new byte[54];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bmp.AsSpan(18), 640);
        BitConverter.TryWriteBytes(bmp.AsSpan(22), -480);
        Assert.Equal(new PageSize(640, 480), ImageHeaderReader.ReadSize(new MemoryStream(bmp)));
    }

    [Fact]
    public void 大きなAPPセグメントを読み飛ばすJpeg()
    {
        var jpeg = Encode(200, 300, SKEncodedImageFormat.Jpeg);
        // SOI の直後に 60KB の APP1 を挿入する
        var app1 = new byte[60000];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        app1[2] = (byte)((app1.Length - 2) >> 8);
        app1[3] = (byte)((app1.Length - 2) & 0xFF);
        byte[] data = [.. jpeg[..2], .. app1, .. jpeg[2..]];
        Assert.Equal(new PageSize(200, 300), ImageHeaderReader.ReadSize(new NonSeekableStream(data)));
    }

    [Fact]
    public void 不明な形式は空()
    {
        Assert.True(ImageHeaderReader.ReadSize(new MemoryStream("hello world, not an image"u8.ToArray())).IsEmpty);
    }

    [Fact]
    public void デコードは縮小に対応する()
    {
        var jpeg = Encode(800, 1200, SKEncodedImageFormat.Jpeg);
        var full = ImageDecoder.Decode(jpeg, PageSize.Empty);
        Assert.Equal((800, 1200), (full.Width, full.Height));
        Assert.True(full.IsFullResolution);
        full.Release();

        var small = ImageDecoder.Decode(jpeg, new PageSize(200, 300));
        Assert.True(small.Width >= 200 && small.Width < 800);
        Assert.False(small.IsFullResolution);
        Assert.Equal(new PageSize(800, 1200), small.OriginalSize);
        small.Release();
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
