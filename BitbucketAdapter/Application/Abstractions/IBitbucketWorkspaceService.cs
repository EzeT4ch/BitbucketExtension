using BitbucketAdapter.Domain.Workspaces;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketWorkspaceService
{
    Task<IReadOnlyList<BitbucketWorkspace>> GetAllAsync(CancellationToken cancellationToken);
}
