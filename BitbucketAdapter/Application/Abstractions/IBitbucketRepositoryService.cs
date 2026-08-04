using BitbucketAdapter.Domain.Repositories;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketRepositoryService
{
    Task<IReadOnlyList<BitbucketRepository>> GetAllAsync(
        string workspaceSlug,
        CancellationToken cancellationToken);
}
