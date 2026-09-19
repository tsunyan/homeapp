using HomeApp.Core;

namespace HomeApp.Core.Tests;

public sealed class WorkspaceStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsWorkspace()
    {
        using var directory = new TemporaryDirectory();
        var store = new WorkspaceStore(directory.Path);
        var state = SampleData.CreateWorkspace();
        state.Theme = AppTheme.Dark;
        state.ActiveBoard.Widgets[0].X = 1_234;

        await store.SaveAsync(state);
        var loaded = store.Load();

        Assert.Null(loaded.Warning);
        Assert.Equal(AppTheme.Dark, loaded.State.Theme);
        Assert.Equal(1_234, loaded.State.ActiveBoard.Widgets[0].X);
        Assert.Equal(state.Boards.Count, loaded.State.Boards.Count);
    }

    [Fact]
    public async Task Load_WhenPrimaryIsCorrupt_RestoresBackupAndPreservesBadFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new WorkspaceStore(directory.Path);
        var first = SampleData.CreateWorkspace();
        first.ActiveBoard.Name = "バックアップ";
        await store.SaveAsync(first);

        var second = first.Copy();
        second.ActiveBoard.Name = "最新";
        await store.SaveAsync(second);
        await File.WriteAllTextAsync(store.FilePath, "{ invalid json");

        var loaded = store.Load();

        Assert.NotNull(loaded.Warning);
        Assert.Equal("バックアップ", loaded.State.ActiveBoard.Name);
        Assert.Single(Directory.GetFiles(directory.Path, "workspace.recovery-*.json"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"HomeApp.Tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
