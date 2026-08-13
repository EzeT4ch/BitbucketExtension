using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Data;

[DataContract]
internal sealed class PullRequestCommentsData : NotifyPropertyChangedObject
{
    private readonly IBitbucketPullRequestCommentService _pullRequestCommentService;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    private string? _workspaceSlug;
    private string? _repositorySlug;
    private long? _pullRequestId;
    private PullRequestCommentListItem? _activeReplyItem;
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
        CloseActiveReply();
        ClearReplyContext();
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

            SetReplyContext(workspaceSlug, repositorySlug, pullRequestId);
            All.Clear();
            Unresolved.Clear();
            foreach (var comment in comments)
            {
                All.Add(CreateListItem(comment));
                if (!comment.IsResolved && !comment.IsDeleted)
                {
                    Unresolved.Add(CreateListItem(comment));
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
        CloseActiveReply();
        ClearReplyContext();
        Unresolved.Clear();
        All.Clear();
        AllHeader = "Todos los comentarios (0)";
        TabTitle = "Comentarios";
        Status = "Seleccioná un Pull Request para cargar sus comentarios.";
    }

    private PullRequestCommentListItem CreateListItem(BitbucketPullRequestComment comment) =>
        new(comment, OpenReply, CancelReply, SendReplyAsync);

    private void OpenReply(PullRequestCommentListItem item)
    {
        if (!item.CanReply)
        {
            return;
        }

        if (!ReferenceEquals(_activeReplyItem, item))
        {
            CloseActiveReply();
            _activeReplyItem = item;
        }

        item.OpenReplyEditor();
    }

    private void CancelReply(PullRequestCommentListItem item)
    {
        if (ReferenceEquals(_activeReplyItem, item))
        {
            _activeReplyItem = null;
        }

        item.CloseReplyEditor(clearDraft: true);
    }

    private async Task SendReplyAsync(
        PullRequestCommentListItem item,
        string markdownContent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            item.SetReplyStatus("Escribí una respuesta antes de enviarla.");
            return;
        }

        var loadVersion = Volatile.Read(ref _loadVersion);
        var workspaceSlug = _workspaceSlug;
        var repositorySlug = _repositorySlug;
        var pullRequestId = _pullRequestId;
        if (string.IsNullOrWhiteSpace(workspaceSlug) ||
            string.IsNullOrWhiteSpace(repositorySlug) ||
            pullRequestId is null)
        {
            item.SetReplyStatus("El Pull Request cambió. Volvé a abrir la respuesta.");
            return;
        }

        item.SetReplyStatus("Enviando respuesta...");
        try
        {
            await _pullRequestCommentService.CreateReplyAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId.Value,
                item.Id,
                markdownContent,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(loadVersion))
            {
                item.SetReplyStatus("Se canceló el envío de la respuesta.");
            }

            return;
        }
        catch (Exception exception)
        {
            if (IsCurrent(loadVersion))
            {
                item.SetReplyStatus($"No se pudo enviar la respuesta: {exception.Message}");
            }

            return;
        }

        if (!IsCurrent(loadVersion))
        {
            return;
        }

        _activeReplyItem = null;
        item.CloseReplyEditor(clearDraft: true);
        await LoadAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId.Value,
            cancellationToken);
    }

    private void CloseActiveReply()
    {
        _activeReplyItem?.CloseReplyEditor(clearDraft: true);
        _activeReplyItem = null;
    }

    private void SetReplyContext(string workspaceSlug, string repositorySlug, long pullRequestId)
    {
        _workspaceSlug = workspaceSlug;
        _repositorySlug = repositorySlug;
        _pullRequestId = pullRequestId;
    }

    private void ClearReplyContext()
    {
        _workspaceSlug = null;
        _repositorySlug = null;
        _pullRequestId = null;
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
internal sealed class PullRequestCommentListItem : NotifyPropertyChangedObject
{
    private string _replyContent = string.Empty;
    private string _replyStatus = string.Empty;
    private bool _isReplyEditorOpen;

    public PullRequestCommentListItem(
        BitbucketPullRequestComment comment,
        Action<PullRequestCommentListItem> openReply,
        Action<PullRequestCommentListItem> cancelReply,
        Func<PullRequestCommentListItem, string, CancellationToken, Task> sendReplyAsync)
    {
        Id = comment.Id;
        CanReply = !comment.IsDeleted;
        Content = FormatContent(comment.Content);
        Author = comment.AuthorDisplayName;
        AuthorInitials = BuildInitials(comment.AuthorDisplayName);
        CreatedOn = FormatDate(comment.CreatedOn);
        Location = BuildLocation(comment);
        Thread = comment.ParentId is null
            ? "Comentario principal"
            : $"Respuesta al comentario #{comment.ParentId.Value}";
        State = BuildState(comment);
        OpenReplyCommand = new AsyncCommand((_, _) =>
        {
            openReply(this);
            return Task.CompletedTask;
        })
        {
            CanExecute = CanReply,
        };
        CancelReplyCommand = new AsyncCommand((_, _) =>
        {
            cancelReply(this);
            return Task.CompletedTask;
        });
        SendReplyCommand = new AsyncCommand((parameter, cancellationToken) =>
            sendReplyAsync(this, parameter as string ?? string.Empty, cancellationToken))
        {
            CanExecute = false,
        };
    }

    [DataMember]
    public long Id { get; }

    [DataMember]
    public bool CanReply { get; }

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

    [DataMember]
    public string ReplyContent
    {
        get => _replyContent;
        set
        {
            if (SetProperty(ref _replyContent, value))
            {
                SendReplyCommand.CanExecute = !string.IsNullOrWhiteSpace(value);
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
    public AsyncCommand OpenReplyCommand { get; }

    [DataMember]
    public AsyncCommand CancelReplyCommand { get; }

    [DataMember]
    public AsyncCommand SendReplyCommand { get; }

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
