using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

[DataContract]
internal sealed class PullRequestBrowserData : NotifyPropertyChangedObject
{
    private readonly IBitbucketPullRequestService _pullRequestService;
    private readonly SemaphoreSlim _listLoadSemaphore = new(1, 1);

    private string? _workspaceSlug;
    private string? _repositorySlug;
    private long? _selectedPullRequestId;
    private int _selectedTabIndex;
    private bool _isPullRequestSelectorEnabled;
    private bool _suppressSelectionLoad;
    private string _listStatus = "Seleccioná un repositorio para cargar sus Pull Requests activos.";
    private string _detailTabTitle = "Detalle";
    private long _contextVersion;
    private long _selectionVersion;

    public PullRequestBrowserData(
        IBitbucketPullRequestService pullRequestService,
        PullRequestDetailData detail,
        PullRequestFilesData files,
        PullRequestCommentsData comments)
    {
        _pullRequestService = pullRequestService;
        Detail = detail;
        Files = files;
        Comments = comments;
    }

    [DataMember]
    public string ListTabTitle => "Pull Requests";

    [DataMember]
    public string DetailTabTitle
    {
        get => _detailTabTitle;
        private set => SetProperty(ref _detailTabTitle, value);
    }

    [DataMember]
    public ObservableCollection<PullRequestListItem> PullRequests { get; } = [];

    [DataMember]
    public PullRequestDetailData Detail { get; }

    [DataMember]
    public PullRequestFilesData Files { get; }

    [DataMember]
    public PullRequestCommentsData Comments { get; }

    [DataMember]
    public long? SelectedPullRequestId
    {
        get => _selectedPullRequestId;
        set
        {
            if (!SetProperty(ref _selectedPullRequestId, value) || _suppressSelectionLoad)
            {
                return;
            }

            LoadSelectedPullRequest(value);
        }
    }

    [DataMember]
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    [DataMember]
    public bool IsPullRequestSelectorEnabled
    {
        get => _isPullRequestSelectorEnabled;
        private set => SetProperty(ref _isPullRequestSelectorEnabled, value);
    }

    [DataMember]
    public string ListStatus
    {
        get => _listStatus;
        private set => SetProperty(ref _listStatus, value);
    }

    public void SetContext(string? workspaceSlug, string? repositorySlug)
    {
        _workspaceSlug = workspaceSlug;
        _repositorySlug = repositorySlug;
        var contextVersion = Interlocked.Increment(ref _contextVersion);
        ResetPullRequests();

        if (string.IsNullOrWhiteSpace(workspaceSlug) || string.IsNullOrWhiteSpace(repositorySlug))
        {
            ListStatus = "Seleccioná un repositorio para cargar sus Pull Requests activos.";
            return;
        }

        ListStatus = "Cargando Pull Requests activos de Bitbucket...";
        _ = LoadPullRequestsAsync(
            workspaceSlug,
            repositorySlug,
            contextVersion,
            CancellationToken.None);
    }

    public void ClearContext()
    {
        SetContext(null, null);
    }

    private async Task LoadPullRequestsAsync(
        string workspaceSlug,
        string repositorySlug,
        long contextVersion,
        CancellationToken cancellationToken)
    {
        var lockTaken = false;
        try
        {
            await _listLoadSemaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            if (!IsCurrentContext(contextVersion))
            {
                return;
            }

            var pullRequests = await _pullRequestService.GetOpenAsync(
                workspaceSlug,
                repositorySlug,
                cancellationToken);
            if (!IsCurrentContext(contextVersion))
            {
                return;
            }

            PullRequests.Clear();
            foreach (var pullRequest in pullRequests)
            {
                PullRequests.Add(new PullRequestListItem(pullRequest));
            }

            IsPullRequestSelectorEnabled = pullRequests.Count > 0;
            ListStatus = pullRequests.Count switch
            {
                0 => $"No hay Pull Requests activos en {repositorySlug}.",
                1 => "Se encontró 1 Pull Request activo.",
                _ => $"Se encontraron {pullRequests.Count} Pull Requests activos.",
            };
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentContext(contextVersion))
            {
                ListStatus = "Se canceló la carga de Pull Requests de Bitbucket.";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentContext(contextVersion))
            {
                ResetPullRequests();
                ListStatus = $"No se pudieron cargar los Pull Requests de Bitbucket: {exception.Message}";
            }
        }
        finally
        {
            if (lockTaken)
            {
                _listLoadSemaphore.Release();
            }
        }
    }

    private void LoadSelectedPullRequest(long? pullRequestId)
    {
        var selectionVersion = Interlocked.Increment(ref _selectionVersion);
        ResetSelectionData();

        if (pullRequestId is null or <= 0 ||
            string.IsNullOrWhiteSpace(_workspaceSlug) ||
            string.IsNullOrWhiteSpace(_repositorySlug))
        {
            return;
        }

        SelectedTabIndex = 1;
        DetailTabTitle = PullRequests
            .FirstOrDefault(pullRequest => pullRequest.Id == pullRequestId.Value)
            ?.TabTitle ?? $"PR #{pullRequestId.Value}";
        _ = LoadSelectedPullRequestAsync(
            _workspaceSlug,
            _repositorySlug,
            pullRequestId.Value,
            Volatile.Read(ref _contextVersion),
            selectionVersion,
            CancellationToken.None);
    }

    private async Task LoadSelectedPullRequestAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long contextVersion,
        long selectionVersion,
        CancellationToken cancellationToken)
    {
        var commentsTask = Comments.LoadAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            cancellationToken);
        var detail = await Detail.LoadAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            cancellationToken);

        if (detail is not null && IsCurrentSelection(pullRequestId, contextVersion, selectionVersion))
        {
            await Files.LoadAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                detail,
                cancellationToken);
        }

        await commentsTask;
    }

    private bool IsCurrentContext(long contextVersion) =>
        contextVersion == Volatile.Read(ref _contextVersion);

    private bool IsCurrentSelection(long pullRequestId, long contextVersion, long selectionVersion) =>
        IsCurrentContext(contextVersion) &&
        selectionVersion == Volatile.Read(ref _selectionVersion) &&
        SelectedPullRequestId == pullRequestId;

    private void ResetPullRequests()
    {
        Interlocked.Increment(ref _selectionVersion);
        IsPullRequestSelectorEnabled = false;
        PullRequests.Clear();
        SetSelectedPullRequestWithoutLoading(null);
        SelectedTabIndex = 0;
        DetailTabTitle = "Detalle";
        ResetSelectionData();
    }

    private void ResetSelectionData()
    {
        Detail.Reset();
        Files.Reset();
        Comments.Reset();
    }

    private void SetSelectedPullRequestWithoutLoading(long? pullRequestId)
    {
        _suppressSelectionLoad = true;
        try
        {
            SelectedPullRequestId = pullRequestId;
        }
        finally
        {
            _suppressSelectionLoad = false;
        }
    }
}

[DataContract]
internal sealed class PullRequestListItem
{
    public PullRequestListItem(BitbucketPullRequestSummary pullRequest)
    {
        Id = pullRequest.Id;
        DisplayTitle = pullRequest.Title;
        TabTitle = $"#{pullRequest.Id} · {pullRequest.Title}";
        BranchRoute = $"{pullRequest.SourceBranch} → {pullRequest.DestinationBranch}";
        Metadata = $"#{pullRequest.Id} · {pullRequest.AuthorDisplayName} · actualizado {FormatDate(pullRequest.UpdatedOn)}";
        State = pullRequest.IsDraft ? $"BORRADOR · {pullRequest.State}" : pullRequest.State;
        Activity = $"{pullRequest.CommentCount} comentarios · {pullRequest.TaskCount} tareas";
    }

    [DataMember]
    public long Id { get; }

    [DataMember]
    public string DisplayTitle { get; }

    [DataMember]
    public string TabTitle { get; }

    [DataMember]
    public string BranchRoute { get; }

    [DataMember]
    public string State { get; }

    [DataMember]
    public string Metadata { get; }

    [DataMember]
    public string Activity { get; }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "sin fecha";
}
