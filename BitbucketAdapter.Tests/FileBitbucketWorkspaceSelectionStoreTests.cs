using BitbucketAdapter.Infrastructure.Workspaces;

namespace BitbucketAdapter.Tests;

public sealed class FileBitbucketWorkspaceSelectionStoreTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "CommentExtensionBitbucket.Tests",
        Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directoryPath, "bitbucket-workspace.json");

    [Fact]
    public async Task GetAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var store = new FileBitbucketWorkspaceSelectionStore(FilePath);

        var result = await store.GetAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_PersistsTrimmedSlugThatCanBeReadLater()
    {
        var store = new FileBitbucketWorkspaceSelectionStore(FilePath);

        await store.SaveAsync("  team-workspace  ", CancellationToken.None);
        var result = await new FileBitbucketWorkspaceSelectionStore(FilePath)
            .GetAsync(CancellationToken.None);

        Assert.Equal("team-workspace", result);
        Assert.Equal("{\"slug\":\"team-workspace\"}", await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task ClearAsync_RemovesPersistedSelection()
    {
        var store = new FileBitbucketWorkspaceSelectionStore(FilePath);
        await store.SaveAsync("team-workspace", CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.GetAsync(CancellationToken.None));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task GetAsync_WhenFileIsCorrupt_ReturnsNull()
    {
        Directory.CreateDirectory(_directoryPath);
        await File.WriteAllTextAsync(FilePath, "not-json");
        var store = new FileBitbucketWorkspaceSelectionStore(FilePath);

        var result = await store.GetAsync(CancellationToken.None);

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
