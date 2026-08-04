using BitbucketAdapter.Domain.PullRequests;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketPullRequestService
{
    Task<IReadOnlyList<BitbucketPullRequestSummary>> GetOpenAsync(
        string workspaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken);

    Task<BitbucketPullRequestDetail> GetByIdAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken);
}
