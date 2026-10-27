using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Presentation.Bitbucket.PullRequests.Data;
using Xunit;

namespace ClientExtension.Tests;

public sealed class PullRequestCommentsDataTests
{
    [Fact]
    public async Task OpenReply_WhenAnotherEditorIsOpen_ClosesItAndDiscardsItsDraft()
    {
        var service = new StubCommentService
        {
            GetAllHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
                [Comment(1), Comment(2), Comment(3, isDeleted: true)]),
        };
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);

        var first = data.All[0];
        var second = data.All[1];
        var deleted = data.All[2];
        await first.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);
        first.ReplyContent = "draft";
        await second.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);

        Assert.False(first.IsReplyEditorOpen);
        Assert.Equal(string.Empty, first.ReplyContent);
        Assert.True(second.IsReplyEditorOpen);
        Assert.True(second.CanReply);
        Assert.False(deleted.CanReply);
        Assert.False(deleted.OpenReplyCommand.CanExecute);
    }

    [Fact]
    public async Task CancelReply_ClosesEditorAndClearsDraft()
    {
        var service = CreateServiceWith(Comment(1));
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var item = Assert.Single(data.All);
        await item.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);
        item.ReplyContent = "draft";

        await item.CancelReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);

        Assert.False(item.IsReplyEditorOpen);
        Assert.Equal(string.Empty, item.ReplyContent);
        Assert.Equal(string.Empty, item.ReplyStatus);
    }

    [Fact]
    public async Task SendReply_PostsCapturedContextAndMarkdownThenReloadsComments()
    {
        const string markdown = "  **reply**\n\n    code  ";
        var service = new StubCommentService();
        service.GetAllHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
            service.GetAllCalls.Count == 1
                ? [Comment(17)]
                : [Comment(17), Comment(98, parentId: 17)]);
        service.CreateReplyHandler = (_, _) => Task.FromResult(98L);
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var item = Assert.Single(data.All);
        await item.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);
        item.ReplyContent = markdown;
        using var cancellationTokenSource = new CancellationTokenSource();

        await item.SendReplyCommand.ExecuteAsync(markdown, null!, cancellationTokenSource.Token);

        var call = Assert.Single(service.CreateReplyCalls);
        Assert.Equal("team", call.WorkspaceSlug);
        Assert.Equal("repo", call.RepositorySlug);
        Assert.Equal(42, call.PullRequestId);
        Assert.Equal(17, call.ParentCommentId);
        Assert.Equal(markdown, call.MarkdownContent);
        Assert.Equal(cancellationTokenSource.Token, call.CancellationToken);
        Assert.Equal([17L, 98L], data.All.Select(comment => comment.Id));
        Assert.Equal(2, service.GetAllCalls.Count);
    }

    [Fact]
    public async Task SendReply_WhenServiceFails_KeepsEditorAndDraftWithError()
    {
        var service = CreateServiceWith(Comment(17));
        service.CreateReplyHandler = (_, _) => throw new BitbucketPullRequestCommentException("Forbidden");
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var item = Assert.Single(data.All);
        await item.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);
        item.ReplyContent = "Keep this draft";

        await item.SendReplyCommand.ExecuteAsync(item.ReplyContent, null!, CancellationToken.None);

        Assert.True(item.IsReplyEditorOpen);
        Assert.Equal("Keep this draft", item.ReplyContent);
        Assert.Contains("Forbidden", item.ReplyStatus);
        Assert.Single(service.GetAllCalls);
    }

    [Fact]
    public async Task SendReply_WhenSelectionChanges_DoesNotRefreshPreviousPullRequest()
    {
        var createReplyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var completeReply = new SemaphoreSlim(0, 1);
        var service = new StubCommentService
        {
            GetAllHandler = (_, _, pullRequestId, _) => Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
                pullRequestId == 42 ? [Comment(17)] : [Comment(200)]),
            CreateReplyHandler = async (_, _) =>
            {
                createReplyStarted.SetResult();
                await completeReply.WaitAsync();
                return 98;
            },
        };
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var oldItem = Assert.Single(data.All);
        await oldItem.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);
        oldItem.ReplyContent = "Reply to old PR";

        var sendTask = oldItem.SendReplyCommand.ExecuteAsync(
            oldItem.ReplyContent,
            null!,
            CancellationToken.None);
        await createReplyStarted.Task;
        await data.LoadAsync("team", "repo", 43, CancellationToken.None);
        completeReply.Release();
        await sendTask;

        Assert.Equal(200, Assert.Single(data.All).Id);
        Assert.Equal([42L, 43L], service.GetAllCalls.Select(call => call.PullRequestId));
    }

    [Fact]
    public async Task ReplyContent_WhenBlank_DisablesSendCommand()
    {
        var data = new PullRequestCommentsData(CreateServiceWith(Comment(17)), new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var item = Assert.Single(data.All);
        await item.OpenReplyCommand.ExecuteAsync(null, null!, CancellationToken.None);

        item.ReplyContent = " \r\n ";
        Assert.False(item.SendReplyCommand.CanExecute);

        item.ReplyContent = "content";
        Assert.True(item.SendReplyCommand.CanExecute);
    }

    [Fact]
    public async Task Actions_OnlyOwnCommentsCanBeEditedOrDeleted()
    {
        var service = CreateServiceWith(
            Comment(1, authorUuid: "me-123"),
            Comment(2, authorUuid: "{other}"),
            Comment(3, parentId: 1, authorUuid: StubCurrentUserService.CurrentUserUuid),
            Comment(4, isResolved: true),
            Comment(5, isDeleted: true, authorUuid: StubCurrentUserService.CurrentUserUuid));
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());

        await data.LoadAsync("team", "repo", 42, CancellationToken.None);

        var own = data.All.Single(item => item.Id == 1).Actions;
        Assert.True(own.CanEdit);
        Assert.True(own.CanDelete);
        Assert.True(own.CanResolve);
        Assert.False(own.CanReopen);
        var foreign = data.All.Single(item => item.Id == 2).Actions;
        Assert.False(foreign.CanEdit);
        Assert.False(foreign.CanDelete);
        Assert.True(foreign.CanResolve);
        var ownReply = data.All.Single(item => item.Id == 3).Actions;
        Assert.True(ownReply.CanEdit);
        Assert.False(ownReply.CanResolve);
        var resolved = data.All.Single(item => item.Id == 4).Actions;
        Assert.False(resolved.CanResolve);
        Assert.True(resolved.CanReopen);
        Assert.False(data.All.Single(item => item.Id == 5).Actions.HasAnyAction);
    }

    [Fact]
    public async Task Actions_WhenCurrentUserCannotBeLoaded_HidesEditAndDeleteButKeepsComments()
    {
        var service = CreateServiceWith(Comment(1, authorUuid: StubCurrentUserService.CurrentUserUuid));
        var users = new StubCurrentUserService
        {
            Handler = _ => throw new InvalidOperationException("sin usuario"),
        };
        var data = new PullRequestCommentsData(service, users);

        await data.LoadAsync("team", "repo", 42, CancellationToken.None);

        var actions = Assert.Single(data.All).Actions;
        Assert.False(actions.CanEdit);
        Assert.True(actions.CanResolve);
    }

    [Fact]
    public async Task CreateComment_PostsGeneralCommentClearsDraftAndReloads()
    {
        var service = CreateServiceWith(Comment(1));
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        var changed = 0;
        data.CommentsChanged += () => changed++;
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);

        data.NewCommentContent = " ";
        Assert.False(data.CreateCommentCommand.CanExecute);
        data.NewCommentContent = "**nuevo**";
        Assert.True(data.CreateCommentCommand.CanExecute);
        await data.CreateCommentCommand.ExecuteAsync("**nuevo**", null!, CancellationToken.None);

        var call = Assert.Single(service.Writes.Calls);
        Assert.Equal("create", call.Action);
        Assert.Equal(42, call.PullRequestId);
        Assert.Equal("**nuevo**", call.MarkdownContent);
        Assert.Null(call.Anchor);
        Assert.Equal(string.Empty, data.NewCommentContent);
        Assert.Equal(2, service.GetAllCalls.Count);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task CreateComment_WhenServiceFails_KeepsDraftAndShowsError()
    {
        var service = CreateServiceWith(Comment(1));
        service.Writes.Handler = _ => throw new BitbucketPullRequestCommentException("Forbidden");
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        data.NewCommentContent = "borrador";

        await data.CreateCommentCommand.ExecuteAsync("borrador", null!, CancellationToken.None);

        Assert.Equal("borrador", data.NewCommentContent);
        Assert.Contains("Forbidden", data.NewCommentStatus);
        Assert.True(data.CreateCommentCommand.CanExecute);
        Assert.Single(service.GetAllCalls);
    }

    [Fact]
    public async Task EditComment_PrefillsContentAndSavesChanges()
    {
        var service = CreateServiceWith(Comment(1, authorUuid: StubCurrentUserService.CurrentUserUuid));
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var actions = Assert.Single(data.All).Actions;

        await actions.EditCommand.ExecuteAsync(null, null!, CancellationToken.None);
        Assert.True(actions.IsEditorOpen);
        Assert.Equal("Comment 1", actions.EditContent);
        actions.EditContent = "Comment 1 editado";
        await actions.SaveEditCommand.ExecuteAsync(actions.EditContent, null!, CancellationToken.None);

        var call = Assert.Single(service.Writes.Calls);
        Assert.Equal("update", call.Action);
        Assert.Equal(1, call.CommentId);
        Assert.Equal("Comment 1 editado", call.MarkdownContent);
        Assert.Equal(2, service.GetAllCalls.Count);
    }

    [Fact]
    public async Task DeleteComment_RequiresConfirmation()
    {
        var service = CreateServiceWith(Comment(1, authorUuid: StubCurrentUserService.CurrentUserUuid));
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var actions = Assert.Single(data.All).Actions;

        await actions.DeleteCommand.ExecuteAsync(null, null!, CancellationToken.None);
        Assert.True(actions.IsDeleteConfirmationVisible);
        Assert.Empty(service.Writes.Calls);

        await actions.CancelDeleteCommand.ExecuteAsync(null, null!, CancellationToken.None);
        Assert.False(actions.IsDeleteConfirmationVisible);
        Assert.Empty(service.Writes.Calls);

        await actions.DeleteCommand.ExecuteAsync(null, null!, CancellationToken.None);
        await actions.ConfirmDeleteCommand.ExecuteAsync(null, null!, CancellationToken.None);

        var call = Assert.Single(service.Writes.Calls);
        Assert.Equal("delete", call.Action);
        Assert.Equal(1, call.CommentId);
    }

    [Theory]
    [InlineData(false, "resolve")]
    [InlineData(true, "reopen")]
    public async Task ResolveAndReopen_CallExpectedOperation(bool isResolved, string expectedAction)
    {
        var service = CreateServiceWith(Comment(1, isResolved: isResolved));
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var actions = Assert.Single(data.All).Actions;

        var command = isResolved ? actions.ReopenCommand : actions.ResolveCommand;
        await command.ExecuteAsync(null, null!, CancellationToken.None);

        var call = Assert.Single(service.Writes.Calls);
        Assert.Equal(expectedAction, call.Action);
        Assert.Equal(1, call.CommentId);
    }

    [Fact]
    public async Task CommentAction_WhenServiceFails_ShowsErrorWithoutReloading()
    {
        var service = CreateServiceWith(Comment(1));
        service.Writes.Handler = _ => throw new BitbucketPullRequestCommentException("Forbidden");
        var data = new PullRequestCommentsData(service, new StubCurrentUserService());
        await data.LoadAsync("team", "repo", 42, CancellationToken.None);
        var actions = Assert.Single(data.All).Actions;

        await actions.ResolveCommand.ExecuteAsync(null, null!, CancellationToken.None);

        Assert.Contains("Forbidden", actions.Status);
        Assert.False(actions.IsBusy);
        Assert.True(actions.ResolveCommand.CanExecute);
        Assert.Single(service.GetAllCalls);
    }

    private static StubCommentService CreateServiceWith(params BitbucketPullRequestComment[] comments) =>
        new()
        {
            GetAllHandler = (_, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(comments),
        };

    private static BitbucketPullRequestComment Comment(
        long id,
        long? parentId = null,
        bool isDeleted = false,
        bool isResolved = false,
        string? authorUuid = null) =>
        new(
            id,
            parentId,
            $"Comment {id}",
            "Author",
            DateTimeOffset.UtcNow,
            null,
            isDeleted,
            false,
            isResolved,
            null,
            null,
            null,
            authorUuid);

    private sealed class StubCommentService : IBitbucketPullRequestCommentService
    {
        public CommentWriteRecorder Writes { get; } = new();

        public Task<long> CreateAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            string markdownContent,
            BitbucketPullRequestCommentAnchor? anchor,
            CancellationToken cancellationToken) =>
            Writes.CreateAsync(workspaceSlug, repositorySlug, pullRequestId, markdownContent, anchor);

        public Task UpdateAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            long commentId,
            string markdownContent,
            CancellationToken cancellationToken) =>
            Writes.RecordAsync("update", workspaceSlug, repositorySlug, pullRequestId, commentId, markdownContent);

        public Task DeleteAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            long commentId,
            CancellationToken cancellationToken) =>
            Writes.RecordAsync("delete", workspaceSlug, repositorySlug, pullRequestId, commentId);

        public Task ResolveAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            long commentId,
            CancellationToken cancellationToken) =>
            Writes.RecordAsync("resolve", workspaceSlug, repositorySlug, pullRequestId, commentId);

        public Task ReopenAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            long commentId,
            CancellationToken cancellationToken) =>
            Writes.RecordAsync("reopen", workspaceSlug, repositorySlug, pullRequestId, commentId);

        public Task<BitbucketPullRequestCommentImage> GetImageAsync(
            string workspaceSlug,
            string repositorySlug,
            Uri imageUri,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Func<string, string, long, CancellationToken, Task<IReadOnlyList<BitbucketPullRequestComment>>>
            GetAllHandler
        { get; set; } = (_, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([]);

        public Func<ReplyCall, CancellationToken, Task<long>> CreateReplyHandler { get; set; } =
            (_, _) => Task.FromResult(1L);

        public List<GetAllCall> GetAllCalls { get; } = [];

        public List<ReplyCall> CreateReplyCalls { get; } = [];

        public Task<long> CreateReplyAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            long parentCommentId,
            string markdownContent,
            CancellationToken cancellationToken)
        {
            var call = new ReplyCall(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                parentCommentId,
                markdownContent,
                cancellationToken);
            CreateReplyCalls.Add(call);
            return CreateReplyHandler(call, cancellationToken);
        }

        public Task<IReadOnlyList<BitbucketPullRequestComment>> GetAllAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            CancellationToken cancellationToken)
        {
            GetAllCalls.Add(new GetAllCall(workspaceSlug, repositorySlug, pullRequestId));
            return GetAllHandler(workspaceSlug, repositorySlug, pullRequestId, cancellationToken);
        }

        public Task<IReadOnlyList<BitbucketPullRequestComment>> GetUnresolvedForFileAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            string filePath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([]);
    }

    private sealed record GetAllCall(string WorkspaceSlug, string RepositorySlug, long PullRequestId);

    private sealed record ReplyCall(
        string WorkspaceSlug,
        string RepositorySlug,
        long PullRequestId,
        long ParentCommentId,
        string MarkdownContent,
        CancellationToken CancellationToken);
}
