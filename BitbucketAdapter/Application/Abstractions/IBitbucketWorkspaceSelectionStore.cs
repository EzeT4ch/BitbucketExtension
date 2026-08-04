namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketWorkspaceSelectionStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(string workspaceSlug, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
