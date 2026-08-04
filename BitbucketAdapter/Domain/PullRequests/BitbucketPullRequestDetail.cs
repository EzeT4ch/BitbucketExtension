namespace BitbucketAdapter.Domain.PullRequests;

public sealed record BitbucketPullRequestDetail(
    BitbucketPullRequestSummary Summary,
    string Description,
    string Reason,
    IReadOnlyList<string> Reviewers,
    string SourceCommitHash,
    string DestinationCommitHash,
    bool CloseSourceBranch,
    string? HtmlUrl);
