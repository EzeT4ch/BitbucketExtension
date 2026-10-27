using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Application.Abstractions;
using ClientExtension.Presentation.Bitbucket.PullRequests;
using ClientExtension.Presentation.Bitbucket.PullRequests.Data;
using Xunit;

namespace ClientExtension.Tests;

public sealed class PullRequestFilesReplyTests
{
    private const string FilePath = "src/File.cs";
    private const string Diff = """
        diff --git a/src/File.cs b/src/File.cs
        --- a/src/File.cs
        +++ b/src/File.cs
        @@ -1 +1 @@
        -old value
        +new value
        """;

    [Fact]
    public async Task LoadInlineComment_WhenItContainsRepositoryImage_UsesAuthenticatedLocalCopy()
    {
        const string imageUrl = "https://bitbucket.org/team/repo/images/123-image.png";
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
                    [Comment(17, content: $"![diagram]({imageUrl})")]),
            GetImageHandler = (_, _, _, _) => Task.FromResult(new BitbucketPullRequestCommentImage(
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
                "image/png")),
        };
        var data = CreateData(comments);

        await LoadFileAsync(data);

        var row = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        var image = Assert.Single(row.ContentBlocks, block => block.Kind == "Image");
        var localUri = new Uri(image.ImageUrl);
        Assert.True(localUri.IsFile);
        Assert.True(File.Exists(localUri.LocalPath));
        var call = Assert.Single(comments.GetImageCalls);
        Assert.Equal("team", call.WorkspaceSlug);
        Assert.Equal("repo", call.RepositorySlug);
        Assert.Equal(imageUrl, call.ImageUri.AbsoluteUri);
    }

    [Fact]
    public async Task LoadInlineComment_WhenImageDownloadFails_ShowsFallbackInsteadOfEmptyFrame()
    {
        const string imageUrl = "https://bitbucket.org/team/repo/images/456-unavailable.png";
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
                    [Comment(17, content: $"![diagram]({imageUrl})")]),
            GetImageHandler = (_, _, _, _) =>
                throw new BitbucketPullRequestCommentException("Forbidden"),
        };
        var data = CreateData(comments);

        await LoadFileAsync(data);

        var row = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        var fallback = Assert.Single(row.ContentBlocks);
        Assert.Equal("Text", fallback.Kind);
        Assert.Equal("No se pudo cargar la imagen: diagram", fallback.Text);
        Assert.Equal(string.Empty, fallback.ImageUrl);
    }

    [Fact]
    public async Task SendInlineReply_PostsCurrentContextThenReloadsDiffComments()
    {
        const string markdown = "  **reply**\n\n    code  ";
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([Comment(17)]),
        };
        var data = CreateData(comments);
        await LoadFileAsync(data);
        var row = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        await row.OpenReplyCommand!.ExecuteAsync(null, null!, CancellationToken.None);
        row.ReplyContent = markdown;
        using var cancellationTokenSource = new CancellationTokenSource();

        await row.SendReplyCommand!.ExecuteAsync(markdown, null!, cancellationTokenSource.Token);
        await WaitUntilAsync(() => comments.GetFileCommentsCalls.Count == 2);

        var call = Assert.Single(comments.CreateReplyCalls);
        Assert.Equal("team", call.WorkspaceSlug);
        Assert.Equal("repo", call.RepositorySlug);
        Assert.Equal(42, call.PullRequestId);
        Assert.Equal(17, call.ParentCommentId);
        Assert.Equal(markdown, call.MarkdownContent);
        Assert.Equal(cancellationTokenSource.Token, call.CancellationToken);
        var reloadedRow = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        Assert.NotSame(row, reloadedRow);
        Assert.False(reloadedRow.IsReplyEditorOpen);
    }

    [Fact]
    public async Task OpenInlineReply_WhenAnotherCommentIsOpen_DiscardsPreviousDraft()
    {
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
                    [Comment(17), Comment(18, parentId: 17)]),
        };
        var data = CreateData(comments);
        await LoadFileAsync(data);
        var first = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        var second = Assert.Single(first.Replies, item => item.CommentId == 18);

        await first.OpenReplyCommand!.ExecuteAsync(null, null!, CancellationToken.None);
        first.ReplyContent = "draft";
        await second.OpenReplyCommand!.ExecuteAsync(null, null!, CancellationToken.None);

        Assert.False(first.IsReplyEditorOpen);
        Assert.Equal(string.Empty, first.ReplyContent);
        Assert.True(second.IsReplyEditorOpen);
        Assert.Equal("Respuesta", second.State);
        Assert.Equal("Respuesta al comentario #17", second.Thread);
        Assert.Same(second, Assert.Single(first.Replies));
    }

    [Fact]
    public async Task SendInlineReply_WhenBitbucketFails_KeepsDraftAndShowsError()
    {
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([Comment(17)]),
            CreateReplyHandler = (_, _) =>
                throw new BitbucketPullRequestCommentException("Forbidden"),
        };
        var data = CreateData(comments);
        await LoadFileAsync(data);
        var row = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        await row.OpenReplyCommand!.ExecuteAsync(null, null!, CancellationToken.None);
        row.ReplyContent = "Keep this draft";

        await row.SendReplyCommand!.ExecuteAsync(row.ReplyContent, null!, CancellationToken.None);

        Assert.True(row.IsReplyEditorOpen);
        Assert.Equal("Keep this draft", row.ReplyContent);
        Assert.Contains("Forbidden", row.ReplyStatus);
        Assert.Single(comments.GetFileCommentsCalls);
    }

    [Theory]
    [InlineData("Added", null, 1)]
    [InlineData("Removed", 1, null)]
    public async Task SendInlineComment_AnchorsToTheSideOfTheChangedLine(
        string changeType,
        int? expectedFromLine,
        int? expectedToLine)
    {
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([Comment(17)]),
        };
        var data = CreateData(comments);
        var changed = 0;
        data.CommentsChanged += () => changed++;
        await LoadFileAsync(data);
        var row = Assert.Single(data.DiffRows, item => item.RowType == "Code" && item.ChangeType == changeType);
        Assert.True(row.CanAddComment);

        await row.OpenCommentCommand!.ExecuteAsync(null, null!, CancellationToken.None);
        Assert.True(row.IsCommentEditorOpen);
        row.NewCommentContent = "comentario en línea";
        await row.SendCommentCommand!.ExecuteAsync(row.NewCommentContent, null!, CancellationToken.None);
        await WaitUntilAsync(() => comments.GetFileCommentsCalls.Count == 2);

        var call = Assert.Single(comments.Writes.Calls);
        Assert.Equal("create", call.Action);
        Assert.Equal("comentario en línea", call.MarkdownContent);
        Assert.Equal(new BitbucketPullRequestCommentAnchor(FilePath, expectedFromLine, expectedToLine), call.Anchor);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task OpenInlineComment_ClosesOpenReplyEditor()
    {
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([Comment(17)]),
        };
        var data = CreateData(comments);
        await LoadFileAsync(data);
        var commentRow = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        var codeRow = data.DiffRows.First(item => item.CanAddComment);
        await commentRow.OpenReplyCommand!.ExecuteAsync(null, null!, CancellationToken.None);
        commentRow.ReplyContent = "draft";

        await codeRow.OpenCommentCommand!.ExecuteAsync(null, null!, CancellationToken.None);

        Assert.False(commentRow.IsReplyEditorOpen);
        Assert.Equal(string.Empty, commentRow.ReplyContent);
        Assert.True(codeRow.IsCommentEditorOpen);
    }

    [Fact]
    public async Task DiffCommentActions_ResolveReloadsDiffAndNotifies()
    {
        var comments = new StubCommentService
        {
            GetFileCommentsHandler = (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>(
                    [Comment(17, authorUuid: StubCurrentUserService.CurrentUserUuid), Comment(18, parentId: 17)]),
        };
        var data = CreateData(comments);
        var changed = 0;
        data.CommentsChanged += () => changed++;
        await LoadFileAsync(data);
        var row = Assert.Single(data.DiffRows, item => item.CommentId == 17);
        var reply = Assert.Single(row.Replies);
        Assert.True(row.Actions!.CanEdit);
        Assert.True(row.Actions.CanResolve);
        Assert.False(reply.Actions!.CanEdit);
        Assert.False(reply.Actions.CanResolve);

        await row.Actions.ResolveCommand.ExecuteAsync(null, null!, CancellationToken.None);
        await WaitUntilAsync(() => comments.GetFileCommentsCalls.Count == 2);

        var call = Assert.Single(comments.Writes.Calls);
        Assert.Equal("resolve", call.Action);
        Assert.Equal(17, call.CommentId);
        Assert.Equal(1, changed);
    }

    private static PullRequestFilesData CreateData(StubCommentService comments) =>
        new(new StubChangesService(), comments, new StubCurrentUserService(), new StubDiffDocumentService());

    private static async Task LoadFileAsync(PullRequestFilesData data)
    {
        await data.LoadAsync("team", "repo", 42, Detail(), CancellationToken.None);
        data.SelectedPath = FilePath;
        await WaitUntilAsync(() => data.DiffRows.Any(row => row.CommentId == 17));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static BitbucketPullRequestDetail Detail() =>
        new(
            new BitbucketPullRequestSummary(
                42,
                "Title",
                "OPEN",
                "Author",
                "feature",
                "main",
                null,
                null,
                1,
                0,
                false),
            string.Empty,
            string.Empty,
            [],
            "source-revision",
            "destination-revision",
            false,
            null);

    private static BitbucketPullRequestComment Comment(
        long id,
        long? parentId = null,
        string? content = null,
        string? authorUuid = null) =>
        new(
            id,
            parentId,
            content ?? $"Comment {id}",
            "Author",
            DateTimeOffset.UtcNow,
            null,
            false,
            false,
            false,
            FilePath,
            null,
            1,
            authorUuid);

    private sealed class StubChangesService : IBitbucketPullRequestChangesService
    {
        public Task<IReadOnlyList<BitbucketPullRequestFileChange>> GetFilesAsync(
            string workspaceSlug,
            string repositorySlug,
            string sourceCommitHash,
            string destinationCommitHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BitbucketPullRequestFileChange>>(
                [new BitbucketPullRequestFileChange(
                    FilePath,
                    null,
                    "modified",
                    1,
                    1,
                    new BitbucketPullRequestFileVersion(FilePath, "before"),
                    new BitbucketPullRequestFileVersion(FilePath, "after"))]);

        public Task<string> GetFileDiffAsync(
            string workspaceSlug,
            string repositorySlug,
            string sourceCommitHash,
            string destinationCommitHash,
            string filePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(Diff);

        public Task<BitbucketPullRequestFileComparison> GetFileComparisonAsync(
            string workspaceSlug,
            string repositorySlug,
            BitbucketPullRequestFileChange fileChange,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

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

        public Func<string, string, long, string, CancellationToken,
            Task<IReadOnlyList<BitbucketPullRequestComment>>> GetFileCommentsHandler
        { get; set; } =
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([]);

        public Func<ReplyCall, CancellationToken, Task<long>> CreateReplyHandler { get; set; } =
            (_, _) => Task.FromResult(98L);

        public Func<string, string, Uri, CancellationToken, Task<BitbucketPullRequestCommentImage>>
            GetImageHandler
        { get; set; } = (_, _, _, _) =>
                throw new InvalidOperationException("Unexpected image request.");

        public List<GetFileCommentsCall> GetFileCommentsCalls { get; } = [];

        public List<ReplyCall> CreateReplyCalls { get; } = [];

        public List<GetImageCall> GetImageCalls { get; } = [];

        public Task<BitbucketPullRequestCommentImage> GetImageAsync(
            string workspaceSlug,
            string repositorySlug,
            Uri imageUri,
            CancellationToken cancellationToken)
        {
            GetImageCalls.Add(new GetImageCall(workspaceSlug, repositorySlug, imageUri));
            return GetImageHandler(workspaceSlug, repositorySlug, imageUri, cancellationToken);
        }

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
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BitbucketPullRequestComment>>([]);

        public Task<IReadOnlyList<BitbucketPullRequestComment>> GetUnresolvedForFileAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            string filePath,
            CancellationToken cancellationToken)
        {
            GetFileCommentsCalls.Add(new GetFileCommentsCall(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                filePath));
            return GetFileCommentsHandler(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                filePath,
                cancellationToken);
        }
    }

    private sealed class StubDiffDocumentService : IPullRequestDiffDocumentService
    {
        public Task OpenAsync(PullRequestDiffDocument document, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record GetFileCommentsCall(
        string WorkspaceSlug,
        string RepositorySlug,
        long PullRequestId,
        string FilePath);

    private sealed record GetImageCall(string WorkspaceSlug, string RepositorySlug, Uri ImageUri);

    private sealed record ReplyCall(
        string WorkspaceSlug,
        string RepositorySlug,
        long PullRequestId,
        long ParentCommentId,
        string MarkdownContent,
        CancellationToken CancellationToken);
}
