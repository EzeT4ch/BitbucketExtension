using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;

namespace BitbucketAdapter.Infrastructure.Repositories;

public sealed class FileBitbucketRepositorySelectionStore : IBitbucketRepositorySelectionStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommentExtensionBitbucket",
        "bitbucket-repository.json");

    private readonly string _filePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileBitbucketRepositorySelectionStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public async Task<string?> GetAsync(string workspaceSlug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);

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
                var selection = JsonSerializer.Deserialize<RepositorySelectionDto>(json);
                if (string.IsNullOrWhiteSpace(selection?.WorkspaceSlug) ||
                    string.IsNullOrWhiteSpace(selection.RepositorySlug) ||
                    !string.Equals(
                        selection.WorkspaceSlug.Trim(),
                        workspaceSlug.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return selection.RepositorySlug.Trim();
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

    public async Task SaveAsync(
        string workspaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var selection = new RepositorySelectionDto(
                workspaceSlug.Trim(),
                repositorySlug.Trim());
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

    private sealed record RepositorySelectionDto(
        [property: JsonPropertyName("workspaceSlug")] string? WorkspaceSlug,
        [property: JsonPropertyName("repositorySlug")] string? RepositorySlug);
}
