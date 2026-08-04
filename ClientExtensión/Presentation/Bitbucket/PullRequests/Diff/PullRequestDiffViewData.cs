using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Application.Abstractions;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Diff;

[DataContract]
internal sealed class PullRequestDiffViewData : NotifyPropertyChangedObject
{
    private string _title = "Diff de Pull Request";
    private string _path = string.Empty;
    private string _comparison = string.Empty;
    private string _status = string.Empty;
    private string _viewMode = "Unified";

    [DataMember]
    public ObservableCollection<PullRequestDiffRow> Rows { get; } = [];

    [DataMember]
    public ObservableCollection<PullRequestSplitDiffRow> SplitRows { get; } = [];

    [DataMember]
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    [DataMember]
    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    [DataMember]
    public string Comparison
    {
        get => _comparison;
        private set => SetProperty(ref _comparison, value);
    }

    [DataMember]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [DataMember]
    public string ViewMode
    {
        get => _viewMode;
        private set => SetProperty(ref _viewMode, value);
    }

    internal void Load(PullRequestDiffDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var displayPath = string.IsNullOrWhiteSpace(document.AfterPath)
            ? document.BeforePath
            : document.AfterPath;
        Title = $"PR #{document.PullRequestId} · {System.IO.Path.GetFileName(displayPath)}";
        Path = displayPath;
        Comparison = $"{document.BeforeLabel}  →  {document.AfterLabel}";

        var comments = document.Comments
            .Select(comment => new BitbucketPullRequestComment(
                comment.Id,
                null,
                comment.Content,
                comment.AuthorDisplayName,
                comment.CreatedOn,
                comment.UpdatedOn,
                false,
                false,
                false,
                displayPath,
                comment.BeforeLine,
                comment.AfterLine))
            .ToArray();

        Rows.Clear();
        SplitRows.Clear();
        var useSplitView = PullRequestUnifiedDiffBuilder.HasAdditionsAndRemovals(document.RawDiff);
        ViewMode = useSplitView ? "Split" : "Unified";
        if (useSplitView)
        {
            foreach (var row in PullRequestUnifiedDiffBuilder.BuildSplit(document.RawDiff, comments, displayPath))
            {
                SplitRows.Add(row);
            }
        }
        else
        {
            foreach (var row in PullRequestUnifiedDiffBuilder.Build(document.RawDiff, comments, displayPath))
            {
                Rows.Add(row);
            }
        }

        var viewDescription = useSplitView ? "Vista dividida base/PR" : "Vista unificada";
        Status = comments.Length switch
        {
            0 => $"{viewDescription} · no hay comentarios sin resolver en este archivo.",
            1 => $"{viewDescription} · 1 comentario sin resolver mostrado en su línea.",
            _ => $"{viewDescription} · {comments.Length} comentarios sin resolver mostrados en sus líneas.",
        };
    }
}
