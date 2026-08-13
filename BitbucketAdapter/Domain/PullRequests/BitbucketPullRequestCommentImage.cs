namespace BitbucketAdapter.Domain.PullRequests;

public sealed record BitbucketPullRequestCommentImage(
    byte[] Content,
    string ContentType);
