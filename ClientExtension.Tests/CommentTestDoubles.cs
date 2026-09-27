using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using BitbucketAdapter.Domain.Users;

namespace ClientExtension.Tests;

internal sealed class StubCurrentUserService : IBitbucketCurrentUserService
{
    public const string CurrentUserUuid = "{me-123}";

    public Func<CancellationToken, Task<BitbucketUser>> Handler { get; set; } =
        _ => Task.FromResult(new BitbucketUser(CurrentUserUuid, "Yo"));

    public Task<BitbucketUser> GetAsync(CancellationToken cancellationToken) => Handler(cancellationToken);
}

internal sealed record CommentWriteCall(
    string Action,
    string WorkspaceSlug,
    string RepositorySlug,
    long PullRequestId,
    long? CommentId,
    string? MarkdownContent,
    BitbucketPullRequestCommentAnchor? Anchor);

internal sealed class CommentWriteRecorder
{
    public List<CommentWriteCall> Calls { get; } = [];

    public Func<CommentWriteCall, Task> Handler { get; set; } = _ => Task.CompletedTask;

    public async Task<long> CreateAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string markdownContent,
        BitbucketPullRequestCommentAnchor? anchor)
    {
        await RecordAsync(new CommentWriteCall(
            "create",
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            null,
            markdownContent,
            anchor));
        return 500;
    }

    public Task RecordAsync(
        string action,
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        string? markdownContent = null) =>
        RecordAsync(new CommentWriteCall(
            action,
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            commentId,
            markdownContent,
            null));

    private Task RecordAsync(CommentWriteCall call)
    {
        Calls.Add(call);
        return Handler(call);
    }
}
