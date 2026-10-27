using BitbucketAdapter.Domain.PullRequests;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketPullRequestCommentService
{
    Task<BitbucketPullRequestCommentImage> GetImageAsync(
        string workspaceSlug,
        string repositorySlug,
        Uri imageUri,
        CancellationToken cancellationToken);

    Task<long> CreateReplyAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long parentCommentId,
        string markdownContent,
        CancellationToken cancellationToken);

    Task<long> CreateAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string markdownContent,
        BitbucketPullRequestCommentAnchor? anchor,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        string markdownContent,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        CancellationToken cancellationToken);

    Task ResolveAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        CancellationToken cancellationToken);

    Task ReopenAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        CancellationToken cancellationToken);

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
