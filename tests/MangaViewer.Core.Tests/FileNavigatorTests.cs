using MangaViewer.Core.Sources;

namespace MangaViewer.Core.Tests;

public sealed class FileNavigatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mv-nav-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private string Folder(string name, bool withImage)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        if (withImage) System.IO.File.WriteAllBytes(Path.Combine(path, "001.png"), []);
        return path;
    }

    [Fact]
    public void FindsNextAndPreviousInNaturalOrder()
    {
        var v1 = File("vol1.zip");
        var v2 = File("vol2.cbz");
        var v10 = File("vol10.pdf");

        Assert.Equal(v10, FileNavigator.FindSibling(v2, 1));
        Assert.Equal(v1, FileNavigator.FindSibling(v2, -1));
        Assert.Null(FileNavigator.FindSibling(v10, 1));
        Assert.Null(FileNavigator.FindSibling(v1, -1));
    }

    [Fact]
    public void SkipsUnsupportedFilesAndFoldersWithoutImages()
    {
        var v1 = File("vol1.zip");
        File("vol2.txt");
        Folder("vol3", withImage: false);
        var v4 = Folder("vol4", withImage: true);
        var v5 = File("vol5.zip");

        Assert.Equal(v4, FileNavigator.FindSibling(v1, 1));
        Assert.Equal(v5, FileNavigator.FindSibling(v4, 1));
        Assert.Equal(v1, FileNavigator.FindSibling(v4, -1));
        Assert.Equal(v5, FileNavigator.FindSibling(v1, 2));
    }

    [Fact]
    public void FindsSiblingOfImageFolder()
    {
        var a = Folder("a", withImage: true);
        var b = Folder("b", withImage: true);

        Assert.Equal(b, FileNavigator.FindSibling(a + Path.DirectorySeparatorChar, 1));
    }
}
