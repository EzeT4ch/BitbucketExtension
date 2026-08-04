namespace BitbucketAdapter.Domain.PullRequests;

public sealed record BitbucketPullRequestFileChange(
    string Path,
    string? PreviousPath,
    string Status,
    int LinesAdded,
    int LinesRemoved,
    BitbucketPullRequestFileVersion? Before,
    BitbucketPullRequestFileVersion? After);

public sealed record BitbucketPullRequestFileVersion(
    string Path,
    string Revision);

public sealed record BitbucketPullRequestFileSnapshot(
    string Path,
    string Revision,
    string Content);

public sealed record BitbucketPullRequestFileComparison(
    BitbucketPullRequestFileSnapshot? Before,
    BitbucketPullRequestFileSnapshot? After);
