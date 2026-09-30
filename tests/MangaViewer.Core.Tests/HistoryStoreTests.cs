using MangaViewer.Core.Persistence;

namespace MangaViewer.Core.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mv-history-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private HistoryRecord Record(string name, int page, DateTimeOffset at) =>
        new(Path.Combine(_dir, name), page, 100, ViewMode.Spread, ReadingDirection.LeftToRight, true, false, at);

    [Fact]
    public void 保存と読み込み()
    {
        using var store = new HistoryStore(Path.Combine(_dir, "history.db"));
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        store.Save(Record("a.zip", 10, at));
        store.Save(Record("a.zip", 12, at));

        var r = store.Get(Path.Combine(_dir, "a.zip"));
        Assert.NotNull(r);
        Assert.Equal(12, r.LastPage);
        Assert.Equal(ReadingDirection.LeftToRight, r.Direction);
        Assert.True(r.Shift);
        Assert.False(r.CoverSingle);
        Assert.Equal(at, r.LastOpenedAt);
        Assert.Null(store.Get(Path.Combine(_dir, "b.zip")));
    }

    [Fact]
    public void パスの大文字小文字()
    {
        using var store = new HistoryStore(Path.Combine(_dir, "history.db"));
        store.Save(Record("Book.zip", 1, DateTimeOffset.UtcNow));
        var found = store.Get(Path.Combine(_dir, "book.zip"));
        Assert.Equal(OperatingSystem.IsWindows(), found is not null);
    }

    [Fact]
    public void 上限を超えた古い記録を削除する()
    {
        using var store = new HistoryStore(Path.Combine(_dir, "history.db"));
        var baseTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 15; i++)
        {
            store.Save(Record($"{i}.zip", i, baseTime.AddMinutes(i)));
        }
        Assert.Equal(5, store.Prune(10));
        Assert.Equal(10, store.Count());
        Assert.Null(store.Get(Path.Combine(_dir, "4.zip")));
        Assert.NotNull(store.Get(Path.Combine(_dir, "5.zip")));
    }
}
