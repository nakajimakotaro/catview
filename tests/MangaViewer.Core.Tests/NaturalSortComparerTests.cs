using MangaViewer.Core;

namespace MangaViewer.Core.Tests;

public class NaturalSortComparerTests
{
    [Fact]
    public void 数字部分を数値として比較する()
    {
        string[] input = ["10.jpg", "2.jpg", "1.jpg"];
        Assert.Equal(["1.jpg", "2.jpg", "10.jpg"], input.Order(NaturalSortComparer.Instance));
    }

    [Fact]
    public void 大文字小文字を区別しない()
    {
        string[] input = ["b.jpg", "A.jpg", "a2.jpg"];
        Assert.Equal(["A.jpg", "a2.jpg", "b.jpg"], input.Order(NaturalSortComparer.Instance));
    }

    [Fact]
    public void フォルダを含むパス()
    {
        string[] input = ["vol10/1.jpg", "vol2/10.jpg", "vol2/9.jpg", "vol1/1.jpg"];
        Assert.Equal(["vol1/1.jpg", "vol2/9.jpg", "vol2/10.jpg", "vol10/1.jpg"], input.Order(NaturalSortComparer.Instance));
    }

    [Fact]
    public void 桁数の多い数字()
    {
        string[] input = ["99999999999999999999999.jpg", "100000000000000000000000.jpg", "5.jpg"];
        Assert.Equal(["5.jpg", "99999999999999999999999.jpg", "100000000000000000000000.jpg"], input.Order(NaturalSortComparer.Instance));
    }

    [Fact]
    public void 先頭の0()
    {
        string[] input = ["010.jpg", "9.jpg", "001.jpg"];
        Assert.Equal(["001.jpg", "9.jpg", "010.jpg"], input.Order(NaturalSortComparer.Instance));
    }
}
