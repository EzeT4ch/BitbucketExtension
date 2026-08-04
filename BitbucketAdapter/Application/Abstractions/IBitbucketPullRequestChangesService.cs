using BitbucketAdapter.Domain.PullRequests;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketPullRequestChangesService
{
    Task<IReadOnlyList<BitbucketPullRequestFileChange>> GetFilesAsync(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        CancellationToken cancellationToken);

    Task<string> GetFileDiffAsync(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        string filePath,
        CancellationToken cancellationToken);

    Task<BitbucketPullRequestFileComparison> GetFileComparisonAsync(
        string workspaceSlug,
        string repositorySlug,
        BitbucketPullRequestFileChange fileChange,
        CancellationToken cancellationToken);
}
