using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;

namespace BitbucketAdapter.Infrastructure.Workspaces;

public sealed class FileBitbucketWorkspaceSelectionStore : IBitbucketWorkspaceSelectionStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommentExtensionBitbucket",
        "bitbucket-workspace.json");

    private readonly string _filePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileBitbucketWorkspaceSelectionStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                var selection = JsonSerializer.Deserialize<WorkspaceSelectionDto>(json);
                return string.IsNullOrWhiteSpace(selection?.Slug) ? null : selection.Slug.Trim();
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                return null;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveAsync(string workspaceSlug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var selection = new WorkspaceSelectionDto(workspaceSlug.Trim());
            await File.WriteAllTextAsync(
                _filePath,
                JsonSerializer.Serialize(selection),
                cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private sealed record WorkspaceSelectionDto(
        [property: JsonPropertyName("slug")] string? Slug);
}
