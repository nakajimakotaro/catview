using System.IO.Compression;
using System.Text;
using MangaViewer.Core.Sources;
using SkiaSharp;

namespace MangaViewer.Core.Tests;

public sealed class ZipPageSourceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mv-zip-").FullName;

    static ZipPageSourceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateZip(IEnumerable<string> names, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".zip");
        var png = ImageHeaderReaderTests.Encode(10, 20, SKEncodedImageFormat.Png);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create, encoding))
        {
            foreach (var name in names)
            {
                var entry = zip.CreateEntry(name);
                if (name.EndsWith('/')) continue;
                using var s = entry.Open();
                s.Write(png);
            }
        }
        return path;
    }

    /// <summary>ローカルヘッダとセントラルディレクトリの汎用ビットフラグを書き換える。</summary>
    private static void PatchFlags(string path, Func<ushort, ushort> patch)
    {
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            var sig = BitConverter.ToUInt32(bytes, i);
            var flagOffset = sig switch
            {
                0x04034B50 => 6,
                0x02014B50 => 8,
                _ => -1,
            };
            if (flagOffset < 0) continue;
            var flags = BitConverter.ToUInt16(bytes, i + flagOffset);
            BitConverter.TryWriteBytes(bytes.AsSpan(i + flagOffset), patch(flags));
        }
        File.WriteAllBytes(path, bytes);
    }

    private static string[] Names(ZipPageSource source) =>
        Enumerable.Range(0, source.PageCount).Select(source.GetPageName).ToArray();

    [Fact]
    public void 画像以外を無視し自然順に並べる()
    {
        var path = CreateZip(["10.png", "2.png", "readme.txt", "Thumbs.db", "__MACOSX/._1.png", "inner.zip", "sub/", "sub/1.png", "1.png"]);
        using var source = new ZipPageSource(path);
        Assert.Equal(["1.png", "2.png", "10.png", "sub/1.png"], Names(source));
        Assert.Equal(new PageSize(10, 20), source.GetPageSize(0));
    }

    [Fact]
    public void UTF8フラグ付きの日本語名()
    {
        var path = CreateZip(["第1話/01.png"]);
        using var source = new ZipPageSource(path);
        Assert.Equal(["第1話/01.png"], Names(source));
    }

    [Fact]
    public void UTF8フラグなしのUTF8名()
    {
        var path = CreateZip(["第1話/01.png"]);
        PatchFlags(path, f => (ushort)(f & ~0x0800));
        using var source = new ZipPageSource(path);
        Assert.Equal(["第1話/01.png"], Names(source));
    }

    [Fact]
    public void ShiftJIS名()
    {
        var path = CreateZip(["第1話/01.png", "表紙.png"], Encoding.GetEncoding(932));
        using var source = new ZipPageSource(path);
        Assert.Equal(["第1話/01.png", "表紙.png"], Names(source));
    }

    [Fact]
    public void 暗号化ZIPは開けない()
    {
        var path = CreateZip(["1.png"]);
        PatchFlags(path, f => (ushort)(f | 0x0001));
        Assert.Throws<EncryptedFileException>(() => new ZipPageSource(path));
    }

    [Fact]
    public async Task ページ画像を取得できる()
    {
        var path = CreateZip(["1.png"]);
        using var source = new ZipPageSource(path);
        var img = await source.GetPageAsync(0, PageSize.Empty, CancellationToken.None);
        Assert.Equal((10, 20), (img.Width, img.Height));
        img.Release();
    }
}
