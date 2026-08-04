using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

[DataContract]
internal sealed class PullRequestCommentsData : NotifyPropertyChangedObject
{
    private readonly IBitbucketPullRequestCommentService _pullRequestCommentService;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    private string _status = "Seleccioná un Pull Request para cargar sus comentarios.";
    private string _allCommentsHeader = "Todos los comentarios (0)";
    private string _tabTitle = "Comentarios";
    private long _loadVersion;

    public PullRequestCommentsData(IBitbucketPullRequestCommentService pullRequestCommentService)
    {
        _pullRequestCommentService = pullRequestCommentService;
    }

    [DataMember]
    public ObservableCollection<PullRequestCommentListItem> Unresolved { get; } = [];

    [DataMember]
    public ObservableCollection<PullRequestCommentListItem> All { get; } = [];

    [DataMember]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [DataMember]
    public string AllHeader
    {
        get => _allCommentsHeader;
        private set => SetProperty(ref _allCommentsHeader, value);
    }

    [DataMember]
    public string TabTitle
    {
        get => _tabTitle;
        private set => SetProperty(ref _tabTitle, value);
    }

    internal async Task LoadAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken)
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        Status = $"Cargando comentarios del Pull Request #{pullRequestId}...";
        var lockTaken = false;

        try
        {
            await _loadSemaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            if (!IsCurrent(loadVersion))
            {
                return;
            }

            var comments = await _pullRequestCommentService.GetAllAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                cancellationToken);
            if (!IsCurrent(loadVersion))
            {
                return;
            }

            All.Clear();
            Unresolved.Clear();
            foreach (var comment in comments)
            {
                var item = new PullRequestCommentListItem(comment);
                All.Add(item);
                if (!comment.IsResolved && !comment.IsDeleted)
                {
                    Unresolved.Add(item);
                }
            }

            AllHeader = $"Todos los comentarios ({comments.Count})";
            TabTitle = Unresolved.Count == 0 ? "Comentarios" : $"Comentarios ({Unresolved.Count})";
            Status = BuildStatus(comments.Count, Unresolved.Count);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(loadVersion))
            {
                Status = "Se canceló la carga de comentarios.";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(loadVersion))
            {
                ClearContent();
                Status = $"No se pudieron cargar los comentarios: {exception.Message}";
            }
        }
        finally
        {
            if (lockTaken)
            {
                _loadSemaphore.Release();
            }
        }
    }

    internal void Reset()
    {
        Interlocked.Increment(ref _loadVersion);
        ClearContent();
    }

    private bool IsCurrent(long loadVersion) =>
        loadVersion == Volatile.Read(ref _loadVersion);

    private void ClearContent()
    {
        Unresolved.Clear();
        All.Clear();
        AllHeader = "Todos los comentarios (0)";
        TabTitle = "Comentarios";
        Status = "Seleccioná un Pull Request para cargar sus comentarios.";
    }

    private static string BuildStatus(int commentCount, int unresolvedCount) =>
        (commentCount, unresolvedCount) switch
        {
            (0, _) => "Este Pull Request todavía no tiene comentarios.",
            (1, 0) => "1 comentario · no hay comentarios sin resolver.",
            (_, 0) => $"{commentCount} comentarios · no hay comentarios sin resolver.",
            (1, _) => "1 comentario sin resolver.",
            (_, 1) => $"1 sin resolver · {commentCount} en total.",
            _ => $"{unresolvedCount} sin resolver · {commentCount} en total.",
        };
}

[DataContract]
internal sealed class PullRequestCommentListItem
{
    public PullRequestCommentListItem(BitbucketPullRequestComment comment)
    {
        Content = FormatContent(comment.Content);
        Author = comment.AuthorDisplayName;
        AuthorInitials = BuildInitials(comment.AuthorDisplayName);
        CreatedOn = FormatDate(comment.CreatedOn);
        Location = BuildLocation(comment);
        Thread = comment.ParentId is null
            ? "Comentario principal"
            : $"Respuesta al comentario #{comment.ParentId.Value}";
        State = BuildState(comment);
    }

    [DataMember]
    public string Content { get; }

    [DataMember]
    public string Author { get; }

    [DataMember]
    public string AuthorInitials { get; }

    [DataMember]
    public string CreatedOn { get; }

    [DataMember]
    public string Location { get; }

    [DataMember]
    public string Thread { get; }

    [DataMember]
    public string State { get; }

    private static string BuildLocation(BitbucketPullRequestComment comment)
    {
        if (string.IsNullOrWhiteSpace(comment.FilePath))
        {
            return "Comentario general";
        }

        var line = comment.ToLine ?? comment.FromLine;
        var fileName = System.IO.Path.GetFileName(comment.FilePath);
        return line is null ? fileName : $"{fileName} (línea {line.Value})";
    }

    private static string BuildInitials(string author)
    {
        var names = author.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length == 0
            ? "?"
            : string.Concat(names.Take(2).Select(name => char.ToUpperInvariant(name[0])));
    }

    private static string BuildState(BitbucketPullRequestComment comment)
    {
        if (comment.IsDeleted)
        {
            return "Eliminado";
        }

        if (comment.IsPending)
        {
            return comment.IsResolved ? "Pendiente · hilo resuelto" : "Pendiente";
        }

        return comment.IsResolved ? "Resuelto" : "Sin resolver";
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "sin fecha";

    private static string FormatContent(string content) =>
        content.Replace(":blue_circle:", "●", StringComparison.OrdinalIgnoreCase);
}
