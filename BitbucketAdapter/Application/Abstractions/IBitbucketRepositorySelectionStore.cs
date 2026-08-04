namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketRepositorySelectionStore
{
    Task<string?> GetAsync(string workspaceSlug, CancellationToken cancellationToken);

    Task SaveAsync(
        string workspaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
