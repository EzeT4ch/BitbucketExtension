using BitbucketAdapter.Infrastructure.Repositories;

namespace BitbucketAdapter.Tests;

public sealed class FileBitbucketRepositorySelectionStoreTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "CommentExtensionBitbucket.Tests",
        Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directoryPath, "bitbucket-repository.json");

    [Fact]
    public async Task GetAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var store = new FileBitbucketRepositorySelectionStore(FilePath);

        var result = await store.GetAsync("team-workspace", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_PersistsTrimmedSelectionThatCanBeReadLater()
    {
        var store = new FileBitbucketRepositorySelectionStore(FilePath);

        await store.SaveAsync("  team-workspace  ", "  api-repository  ", CancellationToken.None);
        var result = await new FileBitbucketRepositorySelectionStore(FilePath)
            .GetAsync("team-workspace", CancellationToken.None);

        Assert.Equal("api-repository", result);
        Assert.Equal(
            "{\"workspaceSlug\":\"team-workspace\",\"repositorySlug\":\"api-repository\"}",
            await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task GetAsync_WhenSelectionBelongsToAnotherWorkspace_ReturnsNull()
    {
        var store = new FileBitbucketRepositorySelectionStore(FilePath);
        await store.SaveAsync("first-workspace", "api-repository", CancellationToken.None);

        var result = await store.GetAsync("second-workspace", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClearAsync_RemovesPersistedSelection()
    {
        var store = new FileBitbucketRepositorySelectionStore(FilePath);
        await store.SaveAsync("team-workspace", "api-repository", CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.GetAsync("team-workspace", CancellationToken.None));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task GetAsync_WhenFileIsCorrupt_ReturnsNull()
    {
        Directory.CreateDirectory(_directoryPath);
        await File.WriteAllTextAsync(FilePath, "not-json");
        var store = new FileBitbucketRepositorySelectionStore(FilePath);

        var result = await store.GetAsync("team-workspace", CancellationToken.None);

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
