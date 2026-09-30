using MangaViewer.Core;
using MangaViewer.Core.Layout;

namespace MangaViewer.Core.Tests;

public class SpreadLayoutTests
{
    private static string Layout(int count, ViewMode mode = ViewMode.Spread, bool cover = false, bool shift = false, params int[] landscape)
    {
        var flags = Enumerable.Range(0, count).Select(landscape.Contains).ToArray();
        return string.Join(" ", SpreadLayout.Compute(count, flags, mode, cover, shift));
    }

    [Fact]
    public void 仕様例_ずらしOFF() => Assert.Equal("(0,1) (2,3) (4,5)", Layout(6));

    [Fact]
    public void 仕様例_ずらしON() => Assert.Equal("(0) (1,2) (3,4) (5)", Layout(6, shift: true));

    [Fact]
    public void 表紙単独() => Assert.Equal("(0) (1,2) (3,4) (5)", Layout(6, cover: true));

    [Fact]
    public void 表紙単独とずらし() => Assert.Equal("(0) (1) (2,3) (4,5)", Layout(6, cover: true, shift: true));

    [Fact]
    public void 奇数ページの末尾は単独() => Assert.Equal("(0,1) (2,3) (4)", Layout(5));

    [Fact]
    public void 単ページモード() => Assert.Equal("(0) (1) (2)", Layout(3, ViewMode.Single, cover: true, shift: true));

    [Fact]
    public void 横長ページは単独になり以降のペアリングが変わる()
    {
        // 3 が横長：2 は相手が横長なので単独、4 から組み直し
        Assert.Equal("(0,1) (2) (3) (4,5) (6)", Layout(7, landscape: 3));
    }

    [Fact]
    public void 横長ページが組の先頭位置にある場合() =>
        Assert.Equal("(0,1) (2) (3,4) (5)", Layout(6, landscape: 2));

    [Fact]
    public void 表紙が横長() => Assert.Equal("(0) (1,2)", Layout(3, cover: true, landscape: 0));

    [Fact]
    public void ページなし() => Assert.Empty(SpreadLayout.Compute(0, [], ViewMode.Spread, true, true));

    [Fact]
    public void 一ページのみ_表紙単独とずらし() => Assert.Equal("(0)", Layout(1, cover: true, shift: true));

    [Fact]
    public void すべてのページがちょうど一回ずつ含まれる()
    {
        var rnd = new Random(1);
        for (var n = 0; n < 200; n++)
        {
            var count = rnd.Next(0, 40);
            var flags = Enumerable.Range(0, count).Select(_ => rnd.Next(5) == 0).ToArray();
            var units = SpreadLayout.Compute(count, flags, ViewMode.Spread, rnd.Next(2) == 0, rnd.Next(2) == 0);
            Assert.Equal(Enumerable.Range(0, count), units.SelectMany(u => u.Pages));
            Assert.All(units, u => Assert.True(u.Count == 1 || (!flags[u.Start] && !flags[u.End])));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 2)]
    public void 指定ページを含む表示単位を探す(int page, int expectedUnit)
    {
        var units = SpreadLayout.Compute(6, new bool[6], ViewMode.Spread, false, false);
        Assert.Equal(expectedUnit, SpreadLayout.FindUnitIndex(units, page));
    }

    [Fact]
    public void 設定変更時は表示中の若い番号のページを含む単位を表示する()
    {
        // (2,3) を表示中にずらしを ON にすると、ページ 2 を含む (1,2) を表示する
        var before = SpreadLayout.Compute(6, new bool[6], ViewMode.Spread, false, false);
        var anchor = before[1].Start;
        var after = SpreadLayout.Compute(6, new bool[6], ViewMode.Spread, false, true);
        Assert.Equal(new DisplayUnit(1, 2), after[SpreadLayout.FindUnitIndex(after, anchor)]);
    }
}
