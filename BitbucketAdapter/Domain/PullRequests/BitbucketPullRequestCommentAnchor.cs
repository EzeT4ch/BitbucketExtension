namespace BitbucketAdapter.Domain.PullRequests;

public sealed record BitbucketPullRequestCommentAnchor(
    string Path,
    int? FromLine,
    int? ToLine);
