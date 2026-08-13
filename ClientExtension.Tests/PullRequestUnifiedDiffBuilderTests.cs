using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Presentation.Bitbucket.PullRequests;
using Xunit;

namespace ClientExtension.Tests;

public sealed class PullRequestUnifiedDiffBuilderTests
{
    private const string Diff = """
        diff --git a/src/File.cs b/src/File.cs
        --- a/src/File.cs
        +++ b/src/File.cs
        @@ -209,3 +209,3 @@
         context
        -old value
        +new value
         tail
        """;

    [Fact]
    public void Build_WhenCommentTargetsAfterLine_InsertsItAfterAddedRow()
    {
        var comment = CreateComment(id: 1, fromLine: null, toLine: 210);

        var rows = PullRequestUnifiedDiffBuilder.Build(Diff, [comment]);

        var commentIndex = FindCommentIndex(rows, comment.Id);
        Assert.Equal("Added", rows[commentIndex - 1].ChangeType);
        Assert.Equal(210, rows[commentIndex - 1].AfterLine);
        Assert.Null(rows[commentIndex - 1].CommentFromLine);
        Assert.Equal(210, rows[commentIndex - 1].CommentToLine);
        Assert.Equal("Línea 210", rows[commentIndex].Location);
    }

    [Fact]
    public void Build_WhenCommentContainsEmojiAndMarkdownImage_CreatesRenderableBlocks()
    {
        var comment = CreateComment(
            id: 20,
            fromLine: null,
            toLine: 210,
            content: """
                :yellow_circle: Review this

                ![diagram](https://bitbucket.org/team/repo/images/image.png){: data-layout='center' }

                Done ✅
                """);

        var rows = PullRequestUnifiedDiffBuilder.Build(Diff, [comment]);

        var row = Assert.Single(rows, item => item.CommentId == comment.Id);
        Assert.Collection(
            row.ContentBlocks,
            text =>
            {
                Assert.Equal("Text", text.Kind);
                Assert.Contains("🟡 Review this", text.Text);
            },
            image =>
            {
                Assert.Equal("Image", image.Kind);
                Assert.Equal("https://bitbucket.org/team/repo/images/image.png", image.ImageUrl);
                Assert.Equal("diagram", image.AlternativeText);
            },
            text =>
            {
                Assert.Equal("Text", text.Kind);
                Assert.Contains("Done ✅", text.Text);
                Assert.DoesNotContain("data-layout", text.Text);
            });
    }

    [Fact]
    public void Build_WhenCommentHasReplies_GroupsThemInsideTheRootCard()
    {
        var root = CreateComment(id: 30, fromLine: null, toLine: 210);
        var reply = CreateComment(id: 31, fromLine: null, toLine: 210, parentId: root.Id);

        var rows = PullRequestUnifiedDiffBuilder.Build(Diff, [root, reply]);

        var commentRow = Assert.Single(rows, row => row.RowType == "Comment");
        Assert.Equal(root.Id, commentRow.CommentId);
        var nestedReply = Assert.Single(commentRow.Replies);
        Assert.Equal(reply.Id, nestedReply.CommentId);
        Assert.Equal("Respuesta al comentario #30", nestedReply.Thread);
    }

    [Fact]
    public void CommentRow_RepliesContract_IsNotRecursiveForRemoteUi()
    {
        var repliesType = typeof(PullRequestDiffRow)
            .GetProperty(nameof(PullRequestDiffRow.Replies))!
            .PropertyType
            .GetElementType();

        Assert.NotEqual(typeof(PullRequestDiffRow), repliesType);
    }

    [Fact]
    public void Build_WhenCommentTargetsBeforeLine_InsertsItAfterRemovedRow()
    {
        var comment = CreateComment(id: 2, fromLine: 210, toLine: null);

        var rows = PullRequestUnifiedDiffBuilder.Build(Diff, [comment]);

        var commentIndex = FindCommentIndex(rows, comment.Id);
        Assert.Equal("Removed", rows[commentIndex - 1].ChangeType);
        Assert.Equal(210, rows[commentIndex - 1].BeforeLine);
        Assert.Equal(210, rows[commentIndex - 1].CommentFromLine);
        Assert.Null(rows[commentIndex - 1].CommentToLine);
        Assert.Equal("Línea 210", rows[commentIndex].Location);
    }

    [Fact]
    public void Build_WhenTargetLineIsOutsideHunks_KeepsCommentVisibleAtEnd()
    {
        var comment = CreateComment(id: 3, fromLine: null, toLine: 900);

        var rows = PullRequestUnifiedDiffBuilder.Build(Diff, [comment]);

        Assert.Equal("Meta", rows[^2].RowType);
        Assert.Equal("Comentarios fuera del fragmento mostrado", rows[^2].Content);
        Assert.Equal(comment.Id, rows[^1].CommentId);
        Assert.Equal("Línea 900 · fuera del fragmento mostrado", rows[^1].Location);
    }

    [Fact]
    public void Build_WhenFileIsCSharp_ClassifiesCodeTokens()
    {
        const string diff = """
            diff --git a/src/File.cs b/src/File.cs
            --- a/src/File.cs
            +++ b/src/File.cs
            @@ -1 +1 @@
            -public string Name => "old";
            +public string Name => "new";
            """;

        var rows = PullRequestUnifiedDiffBuilder.Build(diff, [], "src/File.cs");

        var addedRow = Assert.Single(rows, row => row.ChangeType == "Added");
        Assert.Contains(addedRow.Tokens, token =>
            token.Classification == "Keyword" && token.Text.Contains("public", StringComparison.Ordinal));
        Assert.Contains(addedRow.Tokens, token =>
            token.Classification == "String" && token.Text.Contains("\"new\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenCSharpLineContainsGenericKeywords_PreservesOriginalText()
    {
        const string source = "\tpublic  Result<string, string> ProcessLotFile(FileRequest fileRequest)";
        var diff = CreateSingleLineDiff("old value", source);

        var rows = PullRequestUnifiedDiffBuilder.Build(diff, [], "src/File.cs");

        var addedRow = Assert.Single(rows, row => row.ChangeType == "Added");
        Assert.Equal(source, addedRow.Content);
        Assert.Equal(source, string.Concat(addedRow.Tokens.Select(token => token.Text)));
        Assert.Contains(addedRow.Tokens, token => token is { Classification: "Keyword", Text: "public" });
        Assert.Equal(2, addedRow.Tokens.Count(token =>
            token is { Classification: "Keyword", Text: "string" }));
    }

    [Fact]
    public void Build_WhenCSharpLineIsIncomplete_PreservesOriginalText()
    {
        const string source = "\tpublic  Result<string,>(";
        var diff = CreateSingleLineDiff("old value", source);

        var rows = PullRequestUnifiedDiffBuilder.Build(diff, [], "src/File.cs");

        var addedRow = Assert.Single(rows, row => row.ChangeType == "Added");
        Assert.Equal(source, addedRow.Content);
        Assert.Equal(source, string.Concat(addedRow.Tokens.Select(token => token.Text)));
    }

    [Fact]
    public void BuildSplit_WhenCSharpLineContainsGenericKeywords_PreservesOriginalText()
    {
        const string source = "\tpublic  Result<string, string> ProcessLotFile(FileRequest fileRequest)";
        var diff = CreateSingleLineDiff("old value", source);

        var rows = PullRequestUnifiedDiffBuilder.BuildSplit(diff, [], "src/File.cs");

        var changedRow = Assert.Single(rows, row => row.Right?.ChangeType == "Added");
        Assert.Equal(source, changedRow.Right?.Content);
        Assert.Equal(source, string.Concat(changedRow.Right!.Tokens.Select(token => token.Text)));
    }

    [Fact]
    public void BuildSplit_PairsRemovedAndAddedLinesInTheSameRow()
    {
        var rows = PullRequestUnifiedDiffBuilder.BuildSplit(Diff, [], "src/File.cs");

        var changedRow = Assert.Single(rows, row =>
            row.Left?.ChangeType == "Removed" && row.Right?.ChangeType == "Added");
        Assert.Equal(210, changedRow.Left?.BeforeLine);
        Assert.Equal(210, changedRow.Right?.AfterLine);
        Assert.True(PullRequestUnifiedDiffBuilder.HasAdditionsAndRemovals(Diff));
    }

    [Fact]
    public void BuildSplit_AnchorsCommentsOnTheMatchingSide()
    {
        var afterComment = CreateComment(id: 10, fromLine: null, toLine: 210);
        var beforeComment = CreateComment(id: 11, fromLine: 210, toLine: null);

        var rows = PullRequestUnifiedDiffBuilder.BuildSplit(
            Diff,
            [afterComment, beforeComment],
            "src/File.cs");

        var commentRows = rows.Where(row => row.RowType == "Comment").ToArray();
        Assert.Contains(commentRows, row => row.Comment?.CommentId == afterComment.Id && row.CommentSide == "Right");
        Assert.Contains(commentRows, row => row.Comment?.CommentId == beforeComment.Id && row.CommentSide == "Left");
    }

    [Fact]
    public void HasAdditionsAndRemovals_WhenFileIsOnlyAdded_UsesUnifiedView()
    {
        const string addedFileDiff = """
            diff --git a/src/NewFile.cs b/src/NewFile.cs
            --- /dev/null
            +++ b/src/NewFile.cs
            @@ -0,0 +1,2 @@
            +public sealed class NewFile
            +{
            """;

        Assert.False(PullRequestUnifiedDiffBuilder.HasAdditionsAndRemovals(addedFileDiff));
    }

    private static int FindCommentIndex(IReadOnlyList<PullRequestDiffRow> rows, long commentId)
    {
        var index = rows.ToList().FindIndex(row => row.CommentId == commentId);
        Assert.True(index > 0);
        return index;
    }

    private static string CreateSingleLineDiff(string before, string after) =>
        string.Join(
            '\n',
            "diff --git a/src/File.cs b/src/File.cs",
            "--- a/src/File.cs",
            "+++ b/src/File.cs",
            "@@ -1 +1 @@",
            $"-{before}",
            $"+{after}");

    private static BitbucketPullRequestComment CreateComment(
        long id,
        int? fromLine,
        int? toLine,
        string content = "Revisar esta línea",
        long? parentId = null) =>
        new(
            id,
            parentId,
            content,
            "Hernan Flores",
            new DateTimeOffset(2026, 7, 31, 12, 24, 0, TimeSpan.Zero),
            null,
            false,
            false,
            false,
            "src/File.cs",
            fromLine,
            toLine);
}
