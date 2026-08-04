namespace BitbucketAdapter.Domain.PullRequests;

public sealed record BitbucketPullRequestSummary(
    long Id,
    string Title,
    string State,
    string AuthorDisplayName,
    string SourceBranch,
    string DestinationBranch,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? UpdatedOn,
    int CommentCount,
    int TaskCount,
    bool IsDraft);
