using System.Globalization;
using System.Net;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Presentation.Bitbucket.PullRequests.Data;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

internal static partial class PullRequestUnifiedDiffBuilder
{
    internal static IReadOnlyList<PullRequestDiffRow> Build(
        string diff,
        IReadOnlyList<BitbucketPullRequestComment> comments,
        string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(comments);

        var diffRows = ParseDiff(diff, filePath);
        if (comments.Count == 0)
        {
            return diffRows;
        }

        var threads = GroupCommentThreads(comments);
        var commentsByRow = new Dictionary<int, List<CommentThread>>();
        var unanchoredComments = new List<CommentThread>();
        foreach (var thread in threads)
        {
            var rowIndex = FindAnchorRow(diffRows, thread.Root);
            if (rowIndex < 0)
            {
                unanchoredComments.Add(thread);
                continue;
            }

            if (!commentsByRow.TryGetValue(rowIndex, out var rowComments))
            {
                rowComments = [];
                commentsByRow.Add(rowIndex, rowComments);
            }

            rowComments.Add(thread);
        }

        var result = new List<PullRequestDiffRow>(diffRows.Count + comments.Count);
        for (var index = 0; index < diffRows.Count; index++)
        {
            result.Add(diffRows[index]);
            if (!commentsByRow.TryGetValue(index, out var rowComments))
            {
                continue;
            }

            result.AddRange(rowComments.Select(thread => CreateCommentRow(thread, false)));
        }

        if (unanchoredComments.Count > 0)
        {
            result.Add(PullRequestDiffRow.CreateMeta("Comentarios fuera del fragmento mostrado"));
            result.AddRange(unanchoredComments.Select(thread => CreateCommentRow(thread, true)));
        }

        return result;
    }

    internal static bool HasAdditionsAndRemovals(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var insideHunk = false;
        var hasAdditions = false;
        var hasRemovals = false;
        foreach (var line in SplitLines(diff))
        {
            if (HunkHeaderRegex().IsMatch(line))
            {
                insideHunk = true;
                continue;
            }

            if (!insideHunk)
            {
                continue;
            }

            hasAdditions |= line.StartsWith('+');
            hasRemovals |= line.StartsWith('-');
            if (hasAdditions && hasRemovals)
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<PullRequestSplitDiffRow> BuildSplit(
        string diff,
        IReadOnlyList<BitbucketPullRequestComment> comments,
        string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(comments);

        var parsedRows = ParseDiff(diff, filePath);
        var splitRows = PairRows(parsedRows);
        if (comments.Count == 0)
        {
            return splitRows;
        }

        var threads = GroupCommentThreads(comments);
        var commentsByRow = new Dictionary<int, List<(CommentThread Thread, string Side)>>();
        var unanchoredComments = new List<CommentThread>();
        foreach (var thread in threads)
        {
            var anchor = FindSplitAnchor(splitRows, thread.Root);
            if (anchor.RowIndex < 0)
            {
                unanchoredComments.Add(thread);
                continue;
            }

            if (!commentsByRow.TryGetValue(anchor.RowIndex, out var rowComments))
            {
                rowComments = [];
                commentsByRow.Add(anchor.RowIndex, rowComments);
            }

            rowComments.Add((thread, anchor.Side));
        }

        var result = new List<PullRequestSplitDiffRow>(splitRows.Count + comments.Count);
        for (var index = 0; index < splitRows.Count; index++)
        {
            result.Add(splitRows[index]);
            if (!commentsByRow.TryGetValue(index, out var rowComments))
            {
                continue;
            }

            result.AddRange(rowComments.Select(item =>
                PullRequestSplitDiffRow.CreateComment(CreateCommentRow(item.Thread, false), item.Side)));
        }

        if (unanchoredComments.Count > 0)
        {
            result.Add(PullRequestSplitDiffRow.CreateMeta("Comentarios fuera del fragmento mostrado"));
            result.AddRange(unanchoredComments.Select(thread => PullRequestSplitDiffRow.CreateComment(
                CreateCommentRow(thread, true),
                thread.Root.ToLine is not null ? "Right" : "Left")));
        }

        return result;
    }

    private static IReadOnlyList<CommentThread> GroupCommentThreads(
        IReadOnlyList<BitbucketPullRequestComment> comments)
    {
        var commentsById = comments
            .GroupBy(comment => comment.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var groupedReplies = new Dictionary<long, List<BitbucketPullRequestComment>>();
        var roots = new List<BitbucketPullRequestComment>();

        foreach (var comment in commentsById.Values.OrderBy(comment => comment.CreatedOn).ThenBy(comment => comment.Id))
        {
            var root = FindThreadRoot(comment, commentsById);
            if (root.Id == comment.Id)
            {
                roots.Add(comment);
                continue;
            }

            if (!groupedReplies.TryGetValue(root.Id, out var replies))
            {
                replies = [];
                groupedReplies.Add(root.Id, replies);
            }

            replies.Add(comment);
        }

        return roots
            .Select(root => new CommentThread(
                root,
                groupedReplies.TryGetValue(root.Id, out var replies) ? replies : []))
            .ToArray();
    }

    private static BitbucketPullRequestComment FindThreadRoot(
        BitbucketPullRequestComment comment,
        IReadOnlyDictionary<long, BitbucketPullRequestComment> commentsById)
    {
        var current = comment;
        var visited = new HashSet<long>();
        while (current.ParentId is long parentId &&
               visited.Add(current.Id) &&
               commentsById.TryGetValue(parentId, out var parent))
        {
            current = parent;
        }

        return current.ParentId is not null && !visited.Add(current.Id)
            ? comment
            : current;
    }

    private sealed record CommentThread(
        BitbucketPullRequestComment Root,
        IReadOnlyList<BitbucketPullRequestComment> Replies);

    private static PullRequestDiffRow CreateCommentRow(CommentThread thread, bool isUnanchored) =>
        PullRequestDiffRow.CreateComment(thread.Root, isUnanchored, thread.Replies);

    private static List<PullRequestSplitDiffRow> PairRows(IReadOnlyList<PullRequestDiffRow> rows)
    {
        var result = new List<PullRequestSplitDiffRow>();
        var removedRows = new List<PullRequestDiffRow>();
        var addedRows = new List<PullRequestDiffRow>();

        foreach (var row in rows)
        {
            if (row.ChangeType == "Removed")
            {
                removedRows.Add(row);
                continue;
            }

            if (row.ChangeType == "Added")
            {
                addedRows.Add(row);
                continue;
            }

            FlushChangedBlock(result, removedRows, addedRows);
            result.Add(row.RowType switch
            {
                "Code" => PullRequestSplitDiffRow.CreateCode(row, row),
                "Hunk" => PullRequestSplitDiffRow.CreateHunk(row.Content),
                _ => PullRequestSplitDiffRow.CreateMeta(row.Content),
            });
        }

        FlushChangedBlock(result, removedRows, addedRows);
        return result;
    }

    private static void FlushChangedBlock(
        List<PullRequestSplitDiffRow> result,
        List<PullRequestDiffRow> removedRows,
        List<PullRequestDiffRow> addedRows)
    {
        var rowCount = Math.Max(removedRows.Count, addedRows.Count);
        for (var index = 0; index < rowCount; index++)
        {
            result.Add(PullRequestSplitDiffRow.CreateCode(
                index < removedRows.Count ? removedRows[index] : null,
                index < addedRows.Count ? addedRows[index] : null));
        }

        removedRows.Clear();
        addedRows.Clear();
    }

    private static (int RowIndex, string Side) FindSplitAnchor(
        IReadOnlyList<PullRequestSplitDiffRow> rows,
        BitbucketPullRequestComment comment)
    {
        if (comment.ToLine is not null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].Right?.AfterLine == comment.ToLine)
                {
                    return (index, "Right");
                }
            }
        }

        if (comment.FromLine is not null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].Left?.BeforeLine == comment.FromLine)
                {
                    return (index, "Left");
                }
            }
        }

        return (-1, string.Empty);
    }

    private static List<PullRequestDiffRow> ParseDiff(string diff, string? filePath)
    {
        var rows = new List<PullRequestDiffRow>();
        var oldLine = 0;
        var newLine = 0;
        var insideHunk = false;

        foreach (var line in SplitLines(diff))
        {
            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success)
            {
                oldLine = ParseLineNumber(hunkMatch.Groups["old"].Value);
                newLine = ParseLineNumber(hunkMatch.Groups["new"].Value);
                insideHunk = true;
                rows.Add(PullRequestDiffRow.CreateHunk(line));
                continue;
            }

            if (!insideHunk || line.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
            {
                rows.Add(PullRequestDiffRow.CreateMeta(line));
                continue;
            }

            if (line.StartsWith('+'))
            {
                rows.Add(PullRequestDiffRow.CreateCode(
                    null,
                    newLine,
                    "+",
                    line[1..],
                    "Added",
                    filePath));
                newLine++;
                continue;
            }

            if (line.StartsWith('-'))
            {
                rows.Add(PullRequestDiffRow.CreateCode(
                    oldLine,
                    null,
                    "−",
                    line[1..],
                    "Removed",
                    filePath));
                oldLine++;
                continue;
            }

            var content = line.StartsWith(' ') ? line[1..] : line;
            rows.Add(PullRequestDiffRow.CreateCode(
                oldLine,
                newLine,
                string.Empty,
                content,
                "Context",
                filePath));
            oldLine++;
            newLine++;
        }

        return rows;
    }

    private static int FindAnchorRow(
        IReadOnlyList<PullRequestDiffRow> rows,
        BitbucketPullRequestComment comment)
    {
        if (comment.ToLine is not null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].AfterLine == comment.ToLine)
                {
                    return index;
                }
            }
        }

        if (comment.FromLine is not null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].BeforeLine == comment.FromLine)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitLines(string diff)
    {
        using var reader = new StringReader(diff);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static int ParseLineNumber(string value) =>
        int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex("^@@ -(?<old>\\d+)(?:,\\d+)? \\+(?<new>\\d+)(?:,\\d+)? @@")]
    private static partial Regex HunkHeaderRegex();
}

[DataContract]
internal sealed class PullRequestSplitDiffRow
{
    private PullRequestSplitDiffRow(
        string rowType,
        string content,
        PullRequestDiffRow? left = null,
        PullRequestDiffRow? right = null,
        PullRequestDiffRow? comment = null,
        string commentSide = "")
    {
        RowType = rowType;
        Content = content;
        Left = left;
        Right = right;
        Comment = comment;
        CommentSide = commentSide;
    }

    [DataMember]
    public string RowType { get; }

    [DataMember]
    public string Content { get; }

    [DataMember]
    public PullRequestDiffRow? Left { get; }

    [DataMember]
    public PullRequestDiffRow? Right { get; }

    [DataMember]
    public PullRequestDiffRow? Comment { get; }

    [DataMember]
    public string CommentSide { get; }

    internal static PullRequestSplitDiffRow CreateCode(
        PullRequestDiffRow? left,
        PullRequestDiffRow? right) =>
        new("Code", string.Empty, left, right);

    internal static PullRequestSplitDiffRow CreateHunk(string content) =>
        new("Hunk", content);

    internal static PullRequestSplitDiffRow CreateMeta(string content) =>
        new("Meta", content);

    internal static PullRequestSplitDiffRow CreateComment(
        PullRequestDiffRow comment,
        string side) =>
        new("Comment", string.Empty, comment: comment, commentSide: side);
}

internal interface IPullRequestReplyTarget
{
    long? CommentId { get; }

    bool CanReply { get; }

    string ReplyContent { get; }

    void OpenReplyEditor();

    void CloseReplyEditor(bool clearDraft);

    void SetReplyStatus(string status);
}

[DataContract]
internal sealed class PullRequestDiffRow : NotifyPropertyChangedObject, IPullRequestReplyTarget
{
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[(?<alt>[^\]]*)\]\((?<url>https://[^)\s]+)\)(?:\{:[^}]*\})?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> EmojiShortcodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [":yellow_circle:"] = "🟡",
            [":blue_circle:"] = "🔵",
            [":red_circle:"] = "🔴",
            [":green_circle:"] = "🟢",
            [":orange_circle:"] = "🟠",
            [":purple_circle:"] = "🟣",
            [":white_circle:"] = "⚪",
            [":black_circle:"] = "⚫",
            [":white_check_mark:"] = "✅",
            [":warning:"] = "⚠️",
            [":x:"] = "❌",
            [":thumbsup:"] = "👍",
            [":rocket:"] = "🚀",
            [":eyes:"] = "👀",
        };

    private Action<IPullRequestReplyTarget>? _openReply;
    private Action<IPullRequestReplyTarget>? _cancelReply;
    private Func<IPullRequestReplyTarget, string, CancellationToken, Task>? _sendReplyAsync;
    private string _replyContent = string.Empty;
    private string _replyStatus = string.Empty;
    private bool _isReplyEditorOpen;
    private bool _canReply;
    private Action<PullRequestDiffRow>? _openComment;
    private Action<PullRequestDiffRow>? _cancelComment;
    private Func<PullRequestDiffRow, string, CancellationToken, Task>? _sendCommentAsync;
    private string _newCommentContent = string.Empty;
    private string _newCommentStatus = string.Empty;
    private bool _isCommentEditorOpen;
    private bool _canAddComment;
    private PullRequestCommentActions? _actions;

    private PullRequestDiffRow(
        string rowType,
        string changeType,
        int? beforeLine,
        int? afterLine,
        string marker,
        string content,
        IReadOnlyList<PullRequestDiffToken>? tokens = null,
        long? commentId = null,
        string author = "",
        string authorInitials = "",
        string createdOn = "",
        string state = "",
        string location = "",
        string thread = "",
        IReadOnlyList<PullRequestDiffReply>? replies = null,
        BitbucketPullRequestComment? sourceComment = null)
    {
        SourceComment = sourceComment;
        RowType = rowType;
        ChangeType = changeType;
        BeforeLine = beforeLine;
        AfterLine = afterLine;
        BeforeLineText = FormatLineNumber(beforeLine);
        AfterLineText = FormatLineNumber(afterLine);
        CommentFromLine = beforeLine;
        CommentToLine = afterLine;
        CanComment = rowType == "Code" && (CommentFromLine is not null || CommentToLine is not null);
        Marker = marker;
        Content = rowType == "Comment" ? FormatContent(content) : content;
        ContentBlocks = rowType == "Comment" ? BuildContentBlocks(content) : [];
        Tokens = tokens?.ToArray() ?? [];
        CommentId = commentId;
        Author = author;
        AuthorInitials = authorInitials;
        CreatedOn = createdOn;
        State = state;
        Location = location;
        Thread = thread;
        Replies = replies?.ToArray() ?? [];
    }

    internal BitbucketPullRequestComment? SourceComment { get; }

    [DataMember]
    public string RowType { get; }

    [DataMember]
    public string ChangeType { get; }

    internal int? BeforeLine { get; }

    internal int? AfterLine { get; }

    [DataMember]
    public string BeforeLineText { get; }

    [DataMember]
    public string AfterLineText { get; }

    [DataMember]
    public int? CommentFromLine { get; }

    [DataMember]
    public int? CommentToLine { get; }

    [DataMember]
    public bool CanComment { get; }

    [DataMember]
    public string Marker { get; }

    [DataMember]
    public string Content { get; }

    [DataMember]
    public PullRequestCommentContentBlock[] ContentBlocks { get; }

    [DataMember]
    public PullRequestDiffToken[] Tokens { get; }

    [DataMember]
    public long? CommentId { get; }

    [DataMember]
    public string Author { get; }

    [DataMember]
    public string AuthorInitials { get; }

    [DataMember]
    public string CreatedOn { get; }

    [DataMember]
    public string State { get; }

    [DataMember]
    public string Location { get; }

    [DataMember]
    public string Thread { get; }

    [DataMember]
    public PullRequestDiffReply[] Replies { get; }

    [DataMember]
    public bool CanReply
    {
        get => _canReply;
        private set => SetProperty(ref _canReply, value);
    }

    [DataMember]
    public string ReplyContent
    {
        get => _replyContent;
        set
        {
            if (SetProperty(ref _replyContent, value) && SendReplyCommand is not null)
            {
                SendReplyCommand.CanExecute = CanReply && !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    [DataMember]
    public string ReplyStatus
    {
        get => _replyStatus;
        private set => SetProperty(ref _replyStatus, value);
    }

    [DataMember]
    public bool IsReplyEditorOpen
    {
        get => _isReplyEditorOpen;
        private set => SetProperty(ref _isReplyEditorOpen, value);
    }

    [DataMember]
    public AsyncCommand? OpenReplyCommand { get; private set; }

    [DataMember]
    public AsyncCommand? CancelReplyCommand { get; private set; }

    [DataMember]
    public AsyncCommand? SendReplyCommand { get; private set; }

    [DataMember]
    public PullRequestCommentActions? Actions
    {
        get => _actions;
        private set => SetProperty(ref _actions, value);
    }

    [DataMember]
    public bool CanAddComment
    {
        get => _canAddComment;
        private set => SetProperty(ref _canAddComment, value);
    }

    [DataMember]
    public string NewCommentContent
    {
        get => _newCommentContent;
        set
        {
            if (SetProperty(ref _newCommentContent, value) && SendCommentCommand is not null)
            {
                SendCommentCommand.CanExecute = CanAddComment && !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    [DataMember]
    public string NewCommentStatus
    {
        get => _newCommentStatus;
        private set => SetProperty(ref _newCommentStatus, value);
    }

    [DataMember]
    public bool IsCommentEditorOpen
    {
        get => _isCommentEditorOpen;
        private set => SetProperty(ref _isCommentEditorOpen, value);
    }

    [DataMember]
    public AsyncCommand? OpenCommentCommand { get; private set; }

    [DataMember]
    public AsyncCommand? CancelCommentCommand { get; private set; }

    [DataMember]
    public AsyncCommand? SendCommentCommand { get; private set; }

    internal void ConfigureActions(PullRequestCommentActions actions)
    {
        if (CommentId is null or <= 0)
        {
            return;
        }

        Actions = actions;
    }

    internal void ConfigureComment(
        Action<PullRequestDiffRow> openComment,
        Action<PullRequestDiffRow> cancelComment,
        Func<PullRequestDiffRow, string, CancellationToken, Task> sendCommentAsync)
    {
        if (!CanComment)
        {
            return;
        }

        _openComment = openComment;
        _cancelComment = cancelComment;
        _sendCommentAsync = sendCommentAsync;
        OpenCommentCommand ??= new AsyncCommand((_, _) =>
        {
            _openComment?.Invoke(this);
            return Task.CompletedTask;
        });
        CancelCommentCommand ??= new AsyncCommand((_, _) =>
        {
            _cancelComment?.Invoke(this);
            return Task.CompletedTask;
        });
        SendCommentCommand ??= new AsyncCommand((parameter, cancellationToken) =>
            _sendCommentAsync?.Invoke(
                this,
                parameter as string ?? NewCommentContent,
                cancellationToken) ?? Task.CompletedTask);
        CanAddComment = true;
        OpenCommentCommand.CanExecute = true;
        SendCommentCommand.CanExecute = !string.IsNullOrWhiteSpace(NewCommentContent);
    }

    internal BitbucketPullRequestCommentAnchor? BuildCommentAnchor(string filePath)
    {
        if (!CanComment || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return ChangeType == "Removed"
            ? BeforeLine is null ? null : new BitbucketPullRequestCommentAnchor(filePath, BeforeLine, null)
            : AfterLine is null ? null : new BitbucketPullRequestCommentAnchor(filePath, null, AfterLine);
    }

    internal void OpenCommentEditor()
    {
        NewCommentStatus = string.Empty;
        IsCommentEditorOpen = true;
    }

    internal void CloseCommentEditor(bool clearDraft)
    {
        IsCommentEditorOpen = false;
        NewCommentStatus = string.Empty;
        if (clearDraft)
        {
            NewCommentContent = string.Empty;
        }
    }

    internal void SetCommentStatus(string status)
    {
        NewCommentStatus = status;
    }

    internal void ConfigureReply(
        Action<IPullRequestReplyTarget> openReply,
        Action<IPullRequestReplyTarget> cancelReply,
        Func<IPullRequestReplyTarget, string, CancellationToken, Task> sendReplyAsync)
    {
        if (CommentId is null or <= 0)
        {
            return;
        }

        _openReply = openReply;
        _cancelReply = cancelReply;
        _sendReplyAsync = sendReplyAsync;
        OpenReplyCommand ??= new AsyncCommand((_, _) =>
        {
            _openReply?.Invoke(this);
            return Task.CompletedTask;
        });
        CancelReplyCommand ??= new AsyncCommand((_, _) =>
        {
            _cancelReply?.Invoke(this);
            return Task.CompletedTask;
        });
        SendReplyCommand ??= new AsyncCommand((parameter, cancellationToken) =>
            _sendReplyAsync?.Invoke(
                this,
                parameter as string ?? string.Empty,
                cancellationToken) ?? Task.CompletedTask);
        CanReply = true;
        OpenReplyCommand.CanExecute = true;
        SendReplyCommand.CanExecute = !string.IsNullOrWhiteSpace(ReplyContent);
    }

    internal void OpenReplyEditor()
    {
        ReplyStatus = string.Empty;
        IsReplyEditorOpen = true;
    }

    internal void CloseReplyEditor(bool clearDraft)
    {
        IsReplyEditorOpen = false;
        ReplyStatus = string.Empty;
        if (clearDraft)
        {
            ReplyContent = string.Empty;
        }
    }

    internal void SetReplyStatus(string status)
    {
        ReplyStatus = status;
    }

    void IPullRequestReplyTarget.OpenReplyEditor() => OpenReplyEditor();

    void IPullRequestReplyTarget.CloseReplyEditor(bool clearDraft) => CloseReplyEditor(clearDraft);

    void IPullRequestReplyTarget.SetReplyStatus(string status) => SetReplyStatus(status);

    internal static PullRequestDiffRow CreateCode(
        int? beforeLine,
        int? afterLine,
        string marker,
        string content,
        string changeType,
        string? filePath) =>
        new(
            "Code",
            changeType,
            beforeLine,
            afterLine,
            marker,
            content,
            PullRequestSyntaxHighlighter.Highlight(content, filePath));

    internal static PullRequestDiffRow CreateHunk(string content) =>
        new("Hunk", string.Empty, null, null, string.Empty, content);

    internal static PullRequestDiffRow CreateMeta(string content) =>
        new("Meta", string.Empty, null, null, string.Empty, content);

    internal static PullRequestDiffRow CreateComment(
        BitbucketPullRequestComment comment,
        bool isUnanchored,
        IReadOnlyList<BitbucketPullRequestComment>? replies = null)
    {
        var line = comment.ToLine ?? comment.FromLine;
        var location = line is null
            ? "Comentario del archivo"
            : isUnanchored
                ? $"Línea {line.Value} · fuera del fragmento mostrado"
                : $"Línea {line.Value}";

        var replyRows = replies?
            .Select(PullRequestDiffReply.Create)
            .ToArray() ?? [];

        return new PullRequestDiffRow(
            "Comment",
            string.Empty,
            null,
            null,
            string.Empty,
            comment.Content,
            null,
            comment.Id,
            comment.AuthorDisplayName,
            BuildInitials(comment.AuthorDisplayName),
            comment.CreatedOn?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)
                ?? "sin fecha",
            comment.ParentId is null ? "Sin resolver" : "Respuesta",
            location,
            comment.ParentId is null ? string.Empty : $"Respuesta al comentario #{comment.ParentId.Value}",
            replyRows,
            comment);
    }

    private static string FormatLineNumber(int? lineNumber) =>
        lineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    internal static string BuildInitials(string author)
    {
        var names = author.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length == 0
            ? "?"
            : string.Concat(names.Take(2).Select(name => char.ToUpperInvariant(name[0])));
    }

    internal static PullRequestCommentContentBlock[] BuildContentBlocks(string content)
    {
        var blocks = new List<PullRequestCommentContentBlock>();
        var contentIndex = 0;
        foreach (Match match in MarkdownImageRegex.Matches(content))
        {
            AddTextBlock(blocks, content[contentIndex..match.Index]);
            var imageUrl = match.Groups["url"].Value;
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                blocks.Add(PullRequestCommentContentBlock.CreateImage(
                    uri.AbsoluteUri,
                    match.Groups["alt"].Value));
            }
            else
            {
                AddTextBlock(blocks, match.Value);
            }

            contentIndex = match.Index + match.Length;
        }

        AddTextBlock(blocks, content[contentIndex..]);
        return blocks.Count == 0
            ? [PullRequestCommentContentBlock.CreateText(string.Empty)]
            : blocks.ToArray();
    }

    private static void AddTextBlock(
        ICollection<PullRequestCommentContentBlock> blocks,
        string content)
    {
        var text = FormatContent(content).Trim('\r', '\n');
        if (!string.IsNullOrWhiteSpace(text))
        {
            blocks.Add(PullRequestCommentContentBlock.CreateText(text));
        }
    }

    internal static string FormatContent(string content)
    {
        var formattedContent = WebUtility.HtmlDecode(content);
        foreach (var shortcode in EmojiShortcodes)
        {
            formattedContent = formattedContent.Replace(
                shortcode.Key,
                shortcode.Value,
                StringComparison.OrdinalIgnoreCase);
        }

        return formattedContent;
    }
}

[DataContract]
internal sealed class PullRequestDiffReply : NotifyPropertyChangedObject, IPullRequestReplyTarget
{
    private Action<IPullRequestReplyTarget>? _openReply;
    private Action<IPullRequestReplyTarget>? _cancelReply;
    private Func<IPullRequestReplyTarget, string, CancellationToken, Task>? _sendReplyAsync;
    private string _replyContent = string.Empty;
    private string _replyStatus = string.Empty;
    private bool _isReplyEditorOpen;
    private bool _canReply;
    private PullRequestCommentActions? _actions;

    private PullRequestDiffReply(BitbucketPullRequestComment comment)
    {
        SourceComment = comment;
        CommentId = comment.Id;
        Author = comment.AuthorDisplayName;
        AuthorInitials = PullRequestDiffRow.BuildInitials(comment.AuthorDisplayName);
        CreatedOn = comment.CreatedOn?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)
            ?? "sin fecha";
        ContentBlocks = PullRequestDiffRow.BuildContentBlocks(comment.Content);
        Thread = comment.ParentId is null ? string.Empty : $"Respuesta al comentario #{comment.ParentId.Value}";
    }

    [DataMember]
    public long? CommentId { get; }

    [DataMember]
    public string Author { get; }

    [DataMember]
    public string AuthorInitials { get; }

    [DataMember]
    public string CreatedOn { get; }

    [DataMember]
    public PullRequestCommentContentBlock[] ContentBlocks { get; }

    [DataMember]
    public string Thread { get; }

    [DataMember]
    public string State { get; } = "Respuesta";

    internal BitbucketPullRequestComment SourceComment { get; }

    [DataMember]
    public PullRequestCommentActions? Actions
    {
        get => _actions;
        private set => SetProperty(ref _actions, value);
    }

    internal void ConfigureActions(PullRequestCommentActions actions)
    {
        Actions = actions;
    }

    [DataMember]
    public bool CanReply
    {
        get => _canReply;
        private set => SetProperty(ref _canReply, value);
    }

    [DataMember]
    public string ReplyContent
    {
        get => _replyContent;
        set
        {
            if (SetProperty(ref _replyContent, value) && SendReplyCommand is not null)
            {
                SendReplyCommand.CanExecute = CanReply && !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    [DataMember]
    public string ReplyStatus
    {
        get => _replyStatus;
        private set => SetProperty(ref _replyStatus, value);
    }

    [DataMember]
    public bool IsReplyEditorOpen
    {
        get => _isReplyEditorOpen;
        private set => SetProperty(ref _isReplyEditorOpen, value);
    }

    [DataMember]
    public AsyncCommand? OpenReplyCommand { get; private set; }

    [DataMember]
    public AsyncCommand? CancelReplyCommand { get; private set; }

    [DataMember]
    public AsyncCommand? SendReplyCommand { get; private set; }

    internal static PullRequestDiffReply Create(BitbucketPullRequestComment comment) => new(comment);

    internal void ConfigureReply(
        Action<IPullRequestReplyTarget> openReply,
        Action<IPullRequestReplyTarget> cancelReply,
        Func<IPullRequestReplyTarget, string, CancellationToken, Task> sendReplyAsync)
    {
        if (CommentId is null or <= 0)
        {
            return;
        }

        _openReply = openReply;
        _cancelReply = cancelReply;
        _sendReplyAsync = sendReplyAsync;
        OpenReplyCommand ??= new AsyncCommand((_, _) =>
        {
            _openReply?.Invoke(this);
            return Task.CompletedTask;
        });
        CancelReplyCommand ??= new AsyncCommand((_, _) =>
        {
            _cancelReply?.Invoke(this);
            return Task.CompletedTask;
        });
        SendReplyCommand ??= new AsyncCommand((parameter, cancellationToken) =>
            _sendReplyAsync?.Invoke(
                this,
                parameter as string ?? string.Empty,
                cancellationToken) ?? Task.CompletedTask);
        CanReply = true;
        OpenReplyCommand.CanExecute = true;
        SendReplyCommand.CanExecute = !string.IsNullOrWhiteSpace(ReplyContent);
    }

    void IPullRequestReplyTarget.OpenReplyEditor()
    {
        ReplyStatus = string.Empty;
        IsReplyEditorOpen = true;
    }

    void IPullRequestReplyTarget.CloseReplyEditor(bool clearDraft)
    {
        IsReplyEditorOpen = false;
        ReplyStatus = string.Empty;
        if (clearDraft)
        {
            ReplyContent = string.Empty;
        }
    }

    void IPullRequestReplyTarget.SetReplyStatus(string status)
    {
        ReplyStatus = status;
    }
}

[DataContract]
internal sealed class PullRequestCommentContentBlock
{
    private string _kind;
    private string _text;
    private string _imageUrl;

    private PullRequestCommentContentBlock(
        string kind,
        string text,
        string imageUrl,
        string alternativeText)
    {
        _kind = kind;
        _text = text;
        _imageUrl = imageUrl;
        AlternativeText = alternativeText;
    }

    [DataMember]
    public string Kind => _kind;

    [DataMember]
    public string Text => _text;

    [DataMember]
    public string ImageUrl => _imageUrl;

    [DataMember]
    public string AlternativeText { get; }

    internal static PullRequestCommentContentBlock CreateText(string text) =>
        new("Text", text, string.Empty, string.Empty);

    internal static PullRequestCommentContentBlock CreateImage(string imageUrl, string alternativeText) =>
        new(
            "Image",
            string.Empty,
            imageUrl,
            string.IsNullOrWhiteSpace(alternativeText) ? "Imagen del comentario" : alternativeText);

    internal void UseLocalImage(string localImageUrl)
    {
        _imageUrl = localImageUrl;
    }

    internal void SetImageUnavailable()
    {
        _kind = "Text";
        _text = $"No se pudo cargar la imagen: {AlternativeText}";
        _imageUrl = string.Empty;
    }
}
