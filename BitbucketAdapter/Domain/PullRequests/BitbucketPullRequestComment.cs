namespace BitbucketAdapter.Domain.PullRequests;

public sealed record BitbucketPullRequestComment(
    long Id,
    long? ParentId,
    string Content,
    string AuthorDisplayName,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? UpdatedOn,
    bool IsDeleted,
    bool IsPending,
    bool IsResolved,
    string? FilePath,
    int? FromLine,
    int? ToLine);
