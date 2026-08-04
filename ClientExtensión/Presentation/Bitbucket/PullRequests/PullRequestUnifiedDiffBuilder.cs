using System.Globalization;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using BitbucketAdapter.Domain.PullRequests;

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

        var commentsByRow = new Dictionary<int, List<BitbucketPullRequestComment>>();
        var unanchoredComments = new List<BitbucketPullRequestComment>();
        foreach (var comment in comments)
        {
            var rowIndex = FindAnchorRow(diffRows, comment);
            if (rowIndex < 0)
            {
                unanchoredComments.Add(comment);
                continue;
            }

            if (!commentsByRow.TryGetValue(rowIndex, out var rowComments))
            {
                rowComments = [];
                commentsByRow.Add(rowIndex, rowComments);
            }

            rowComments.Add(comment);
        }

        var result = new List<PullRequestDiffRow>(diffRows.Count + comments.Count);
        for (var index = 0; index < diffRows.Count; index++)
        {
            result.Add(diffRows[index]);
            if (!commentsByRow.TryGetValue(index, out var rowComments))
            {
                continue;
            }

            result.AddRange(rowComments.Select(comment => PullRequestDiffRow.CreateComment(comment, false)));
        }

        if (unanchoredComments.Count > 0)
        {
            result.Add(PullRequestDiffRow.CreateMeta("Comentarios fuera del fragmento mostrado"));
            result.AddRange(unanchoredComments.Select(comment => PullRequestDiffRow.CreateComment(comment, true)));
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

        var commentsByRow = new Dictionary<int, List<(BitbucketPullRequestComment Comment, string Side)>>();
        var unanchoredComments = new List<BitbucketPullRequestComment>();
        foreach (var comment in comments)
        {
            var anchor = FindSplitAnchor(splitRows, comment);
            if (anchor.RowIndex < 0)
            {
                unanchoredComments.Add(comment);
                continue;
            }

            if (!commentsByRow.TryGetValue(anchor.RowIndex, out var rowComments))
            {
                rowComments = [];
                commentsByRow.Add(anchor.RowIndex, rowComments);
            }

            rowComments.Add((comment, anchor.Side));
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
                PullRequestSplitDiffRow.CreateComment(item.Comment, item.Side, false)));
        }

        if (unanchoredComments.Count > 0)
        {
            result.Add(PullRequestSplitDiffRow.CreateMeta("Comentarios fuera del fragmento mostrado"));
            result.AddRange(unanchoredComments.Select(comment => PullRequestSplitDiffRow.CreateComment(
                comment,
                comment.ToLine is not null ? "Right" : "Left",
                true)));
        }

        return result;
    }

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
        BitbucketPullRequestComment comment,
        string side,
        bool isUnanchored) =>
        new("Comment", string.Empty, comment: PullRequestDiffRow.CreateComment(comment, isUnanchored), commentSide: side);
}

[DataContract]
internal sealed class PullRequestDiffRow
{
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
        string location = "")
    {
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
        Content = content;
        Tokens = tokens?.ToArray() ?? [];
        CommentId = commentId;
        Author = author;
        AuthorInitials = authorInitials;
        CreatedOn = createdOn;
        State = state;
        Location = location;
    }

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
        bool isUnanchored)
    {
        var line = comment.ToLine ?? comment.FromLine;
        var location = line is null
            ? "Comentario del archivo"
            : isUnanchored
                ? $"Línea {line.Value} · fuera del fragmento mostrado"
                : $"Línea {line.Value}";

        return new PullRequestDiffRow(
            "Comment",
            string.Empty,
            null,
            null,
            string.Empty,
            FormatContent(comment.Content),
            null,
            comment.Id,
            comment.AuthorDisplayName,
            BuildInitials(comment.AuthorDisplayName),
            comment.CreatedOn?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)
                ?? "sin fecha",
            "Sin resolver",
            location);
    }

    private static string FormatLineNumber(int? lineNumber) =>
        lineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string BuildInitials(string author)
    {
        var names = author.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length == 0
            ? "?"
            : string.Concat(names.Take(2).Select(name => char.ToUpperInvariant(name[0])));
    }

    private static string FormatContent(string content) =>
        content.Replace(":blue_circle:", "●", StringComparison.OrdinalIgnoreCase);
}
