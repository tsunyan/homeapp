using System.Text.Json;

namespace HomeApp.Core;

public sealed record LoadResult(WorkspaceState State, string? Warning = null);

public sealed class WorkspaceStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "workspace.json");

    public LoadResult Load()
    {
        if (!File.Exists(FilePath)) return new(SampleData.CreateWorkspace());
        try { return new(Read(FilePath)); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            // Preserve the failed file before allowing a new save; never silently destroy a damaged workspace.
            try
            {
                File.Copy(FilePath, Path.Combine(DirectoryPath, $"workspace.recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                return new(Read(FilePath + ".bak"), "前回の保存を読み込めなかったため、バックアップを復元しました。");
            }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                return new(SampleData.CreateWorkspace(), "保存データを読み込めませんでした。保存先を確認してください。初期ボードを表示しています。");
            }
        }
    }

    private static WorkspaceState Read(string path)
    {
        var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path), WorkspaceJson.Options)
                    ?? throw new InvalidDataException("保存データが空です。");
        state.Validate();
        return state;
    }

    public async Task SaveAsync(WorkspaceState state)
    {
        var copy = state.Copy();
        copy.Validate();
        var json = JsonSerializer.Serialize(copy, WorkspaceJson.Options);
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            await File.WriteAllTextAsync(temp, json);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        finally { _gate.Release(); }
    }
}
