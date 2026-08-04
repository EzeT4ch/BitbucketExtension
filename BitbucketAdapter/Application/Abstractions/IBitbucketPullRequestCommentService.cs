using BitbucketAdapter.Domain.PullRequests;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketPullRequestCommentService
{
    Task<IReadOnlyList<BitbucketPullRequestComment>> GetAllAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BitbucketPullRequestComment>> GetUnresolvedForFileAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string filePath,
        CancellationToken cancellationToken);
}
