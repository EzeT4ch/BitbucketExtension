using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Application.Abstractions;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

[DataContract]
internal sealed class PullRequestBrowserData : NotifyPropertyChangedObject
{
    private readonly IBitbucketPullRequestService _pullRequestService;
    private readonly IBitbucketPullRequestChangesService _pullRequestChangesService;
    private readonly IPullRequestDiffDocumentService _diffDocumentService;
    private readonly Dictionary<string, BitbucketPullRequestFileChange> _changedFilesByPath =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _listLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _detailLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _filesLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _diffLoadSemaphore = new(1, 1);

    private string? _workspaceSlug;
    private string? _repositorySlug;
    private long? _selectedPullRequestId;
    private string? _selectedChangedFilePath;
    private string? _sourceCommitHash;
    private string? _destinationCommitHash;
    private int _selectedTabIndex;
    private bool _isPullRequestSelectorEnabled;
    private bool _suppressSelectionLoad;
    private string _listStatus = "Seleccioná un repositorio para cargar sus Pull Requests activos.";
    private string _detailStatus = "Seleccioná un Pull Request para consultar su detalle.";
    private string _detailTabTitle = "Detalle";
    private string _detailTitle = "Sin Pull Request seleccionado";
    private string _detailState = "—";
    private string _detailAuthor = "—";
    private string _detailBranches = "—";
    private string _detailCreatedOn = "—";
    private string _detailUpdatedOn = "—";
    private string _detailDescription = "—";
    private string _detailReviewers = "—";
    private string _detailCounters = "—";
    private string _detailCommits = "—";
    private string _detailOptions = "—";
    private string _detailUrl = "—";
    private string _detailDiscussionSummary = "Sin Pull Request seleccionado.";
    private string _detailTaskSummary = "Sin Pull Request seleccionado.";
    private string _detailActivitySummary = "Sin Pull Request seleccionado.";
    private string _filesStatus = "Seleccioná un Pull Request para cargar sus archivos modificados.";
    private string _diffStatus = "Seleccioná un archivo modificado para cargar su diff.";
    private string _selectedFileTitle = "Diff";
    private string _selectedFileDiff = "Seleccioná un archivo para visualizar sus cambios.";
    private string? _selectedFileRawDiff;
    private bool _isChangedFileSelectorEnabled;
    private bool _canOpenDiffDocument;
    private bool _suppressChangedFileLoad;
    private long _contextVersion;
    private long _detailVersion;
    private long _filesVersion;
    private long _diffVersion;

    public PullRequestBrowserData(
        IBitbucketPullRequestService pullRequestService,
        IBitbucketPullRequestChangesService pullRequestChangesService,
        IPullRequestDiffDocumentService diffDocumentService)
    {
        _pullRequestService = pullRequestService;
        _pullRequestChangesService = pullRequestChangesService;
        _diffDocumentService = diffDocumentService;
        OpenDiffDocumentCommand = new AsyncCommand(OpenDiffDocumentAsync);
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
    public string ListTitle => "Pull Requests activos";

    [DataMember]
    public string ListDescription =>
        "Seleccioná un Pull Request para cargar la información completa en la solapa Detalle.";

    [DataMember]
    public ObservableCollection<PullRequestListItem> PullRequests { get; } = [];

    [DataMember]
    public ObservableCollection<PullRequestFileListItem> ChangedFiles { get; } = [];

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
    public string? SelectedChangedFilePath
    {
        get => _selectedChangedFilePath;
        set
        {
            if (!SetProperty(ref _selectedChangedFilePath, value) || _suppressChangedFileLoad)
            {
                return;
            }

            LoadSelectedFileDiff(value);
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
    public bool IsChangedFileSelectorEnabled
    {
        get => _isChangedFileSelectorEnabled;
        private set => SetProperty(ref _isChangedFileSelectorEnabled, value);
    }

    [DataMember]
    public string ListStatus
    {
        get => _listStatus;
        private set => SetProperty(ref _listStatus, value);
    }

    [DataMember]
    public string DetailStatus
    {
        get => _detailStatus;
        private set => SetProperty(ref _detailStatus, value);
    }

    [DataMember]
    public string DetailTitle
    {
        get => _detailTitle;
        private set => SetProperty(ref _detailTitle, value);
    }

    [DataMember]
    public string DetailState
    {
        get => _detailState;
        private set => SetProperty(ref _detailState, value);
    }

    [DataMember]
    public string DetailAuthor
    {
        get => _detailAuthor;
        private set => SetProperty(ref _detailAuthor, value);
    }

    [DataMember]
    public string DetailBranches
    {
        get => _detailBranches;
        private set => SetProperty(ref _detailBranches, value);
    }

    [DataMember]
    public string DetailCreatedOn
    {
        get => _detailCreatedOn;
        private set => SetProperty(ref _detailCreatedOn, value);
    }

    [DataMember]
    public string DetailUpdatedOn
    {
        get => _detailUpdatedOn;
        private set => SetProperty(ref _detailUpdatedOn, value);
    }

    [DataMember]
    public string DetailDescription
    {
        get => _detailDescription;
        private set => SetProperty(ref _detailDescription, value);
    }

    [DataMember]
    public string DetailReviewers
    {
        get => _detailReviewers;
        private set => SetProperty(ref _detailReviewers, value);
    }

    [DataMember]
    public string DetailCounters
    {
        get => _detailCounters;
        private set => SetProperty(ref _detailCounters, value);
    }

    [DataMember]
    public string DetailCommits
    {
        get => _detailCommits;
        private set => SetProperty(ref _detailCommits, value);
    }

    [DataMember]
    public string DetailOptions
    {
        get => _detailOptions;
        private set => SetProperty(ref _detailOptions, value);
    }

    [DataMember]
    public string DetailUrl
    {
        get => _detailUrl;
        private set => SetProperty(ref _detailUrl, value);
    }

    [DataMember]
    public string DetailDiscussionSummary
    {
        get => _detailDiscussionSummary;
        private set => SetProperty(ref _detailDiscussionSummary, value);
    }

    [DataMember]
    public string DetailTaskSummary
    {
        get => _detailTaskSummary;
        private set => SetProperty(ref _detailTaskSummary, value);
    }

    [DataMember]
    public string DetailActivitySummary
    {
        get => _detailActivitySummary;
        private set => SetProperty(ref _detailActivitySummary, value);
    }

    [DataMember]
    public string FilesStatus
    {
        get => _filesStatus;
        private set => SetProperty(ref _filesStatus, value);
    }

    [DataMember]
    public string DiffStatus
    {
        get => _diffStatus;
        private set => SetProperty(ref _diffStatus, value);
    }

    [DataMember]
    public string SelectedFileTitle
    {
        get => _selectedFileTitle;
        private set => SetProperty(ref _selectedFileTitle, value);
    }

    [DataMember]
    public string SelectedFileDiff
    {
        get => _selectedFileDiff;
        private set => SetProperty(ref _selectedFileDiff, value);
    }

    [DataMember]
    public bool CanOpenDiffDocument
    {
        get => _canOpenDiffDocument;
        private set => SetProperty(ref _canOpenDiffDocument, value);
    }

    [DataMember]
    public AsyncCommand OpenDiffDocumentCommand { get; }

    public void SetContext(string? workspaceSlug, string? repositorySlug)
    {
        _workspaceSlug = workspaceSlug;
        _repositorySlug = repositorySlug;
        var contextVersion = Interlocked.Increment(ref _contextVersion);
        Interlocked.Increment(ref _detailVersion);
        ResetPullRequests();

        if (string.IsNullOrWhiteSpace(workspaceSlug) || string.IsNullOrWhiteSpace(repositorySlug))
        {
            ListStatus = "Seleccioná un repositorio para cargar sus Pull Requests activos.";
            return;
        }

        ListStatus = "Cargando Pull Requests activos de Bitbucket...";
        _ = LoadPullRequestsAsync(workspaceSlug, repositorySlug, contextVersion, CancellationToken.None);
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
        await _listLoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (contextVersion != Volatile.Read(ref _contextVersion))
            {
                return;
            }

            var pullRequests = await _pullRequestService.GetOpenAsync(
                workspaceSlug,
                repositorySlug,
                cancellationToken);

            if (contextVersion != Volatile.Read(ref _contextVersion))
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
            if (contextVersion == Volatile.Read(ref _contextVersion))
            {
                ListStatus = "Se canceló la carga de Pull Requests de Bitbucket.";
            }

            throw;
        }
        catch (Exception exception)
        {
            if (contextVersion == Volatile.Read(ref _contextVersion))
            {
                ResetPullRequests();
                ListStatus = $"No se pudieron cargar los Pull Requests de Bitbucket: {exception.Message}";
            }
        }
        finally
        {
            _listLoadSemaphore.Release();
        }
    }

    private void LoadSelectedPullRequest(long? pullRequestId)
    {
        var detailVersion = Interlocked.Increment(ref _detailVersion);
        ResetDetail();

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
        DetailStatus = $"Cargando detalle del Pull Request #{pullRequestId.Value}...";
        _ = LoadPullRequestDetailAsync(
            _workspaceSlug,
            _repositorySlug,
            pullRequestId.Value,
            Volatile.Read(ref _contextVersion),
            detailVersion,
            CancellationToken.None);
    }

    private async Task LoadPullRequestDetailAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long contextVersion,
        long detailVersion,
        CancellationToken cancellationToken)
    {
        await _detailLoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentSelection(pullRequestId, contextVersion, detailVersion))
            {
                return;
            }

            var detail = await _pullRequestService.GetByIdAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                cancellationToken);

            if (!IsCurrentSelection(pullRequestId, contextVersion, detailVersion))
            {
                return;
            }

            ApplyDetail(detail);
            LoadChangedFiles(detail, contextVersion, detailVersion);
            DetailStatus = $"Detalle del Pull Request #{pullRequestId}.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentSelection(pullRequestId, contextVersion, detailVersion))
            {
                DetailStatus = "Se canceló la carga del detalle del Pull Request.";
            }

            throw;
        }
        catch (Exception exception)
        {
            if (IsCurrentSelection(pullRequestId, contextVersion, detailVersion))
            {
                ResetDetail();
                SelectedTabIndex = 1;
                DetailStatus = $"No se pudo cargar el detalle del Pull Request: {exception.Message}";
            }
        }
        finally
        {
            _detailLoadSemaphore.Release();
        }
    }

    private bool IsCurrentSelection(long pullRequestId, long contextVersion, long detailVersion) =>
        contextVersion == Volatile.Read(ref _contextVersion) &&
        detailVersion == Volatile.Read(ref _detailVersion) &&
        SelectedPullRequestId == pullRequestId;

    private void LoadChangedFiles(
        BitbucketPullRequestDetail detail,
        long contextVersion,
        long detailVersion)
    {
        _sourceCommitHash = detail.SourceCommitHash;
        _destinationCommitHash = detail.DestinationCommitHash;
        var filesVersion = Interlocked.Increment(ref _filesVersion);
        Interlocked.Increment(ref _diffVersion);
        ClearChangedFiles();

        if (string.IsNullOrWhiteSpace(_workspaceSlug) ||
            string.IsNullOrWhiteSpace(_repositorySlug) ||
            string.IsNullOrWhiteSpace(_sourceCommitHash) ||
            string.IsNullOrWhiteSpace(_destinationCommitHash))
        {
            FilesStatus = "Bitbucket no informó los commits necesarios para consultar los archivos modificados.";
            return;
        }

        FilesStatus = "Cargando archivos modificados...";
        _ = LoadChangedFilesAsync(
            _workspaceSlug,
            _repositorySlug,
            _sourceCommitHash,
            _destinationCommitHash,
            contextVersion,
            detailVersion,
            filesVersion,
            CancellationToken.None);
    }

    private async Task LoadChangedFilesAsync(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        long contextVersion,
        long detailVersion,
        long filesVersion,
        CancellationToken cancellationToken)
    {
        await _filesLoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentFilesLoad(contextVersion, detailVersion, filesVersion))
            {
                return;
            }

            var files = await _pullRequestChangesService.GetFilesAsync(
                workspaceSlug,
                repositorySlug,
                sourceCommitHash,
                destinationCommitHash,
                cancellationToken);

            if (!IsCurrentFilesLoad(contextVersion, detailVersion, filesVersion))
            {
                return;
            }

            ChangedFiles.Clear();
            _changedFilesByPath.Clear();
            foreach (var file in files)
            {
                _changedFilesByPath[file.Path] = file;
                ChangedFiles.Add(new PullRequestFileListItem(file));
            }

            IsChangedFileSelectorEnabled = files.Count > 0;
            FilesStatus = files.Count switch
            {
                0 => "El Pull Request no tiene archivos modificados.",
                1 => "Se encontró 1 archivo modificado.",
                _ => $"Se encontraron {files.Count} archivos modificados.",
            };
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentFilesLoad(contextVersion, detailVersion, filesVersion))
            {
                FilesStatus = "Se canceló la carga de archivos modificados.";
            }

            throw;
        }
        catch (Exception exception)
        {
            if (IsCurrentFilesLoad(contextVersion, detailVersion, filesVersion))
            {
                ClearChangedFiles();
                FilesStatus = $"No se pudieron cargar los archivos modificados: {exception.Message}";
            }
        }
        finally
        {
            _filesLoadSemaphore.Release();
        }
    }

    private void LoadSelectedFileDiff(string? filePath)
    {
        var diffVersion = Interlocked.Increment(ref _diffVersion);
        ResetDiff();

        if (string.IsNullOrWhiteSpace(filePath) ||
            string.IsNullOrWhiteSpace(_workspaceSlug) ||
            string.IsNullOrWhiteSpace(_repositorySlug) ||
            string.IsNullOrWhiteSpace(_sourceCommitHash) ||
            string.IsNullOrWhiteSpace(_destinationCommitHash))
        {
            return;
        }

        SelectedFileTitle = filePath;
        DiffStatus = $"Cargando diff de {filePath}...";
        _ = LoadFileDiffAsync(
            _workspaceSlug,
            _repositorySlug,
            _sourceCommitHash,
            _destinationCommitHash,
            filePath,
            Volatile.Read(ref _contextVersion),
            Volatile.Read(ref _detailVersion),
            diffVersion,
            CancellationToken.None);
    }

    private async Task LoadFileDiffAsync(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        string filePath,
        long contextVersion,
        long detailVersion,
        long diffVersion,
        CancellationToken cancellationToken)
    {
        await _diffLoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentFileSelection(filePath, contextVersion, detailVersion, diffVersion))
            {
                return;
            }

            var diff = await _pullRequestChangesService.GetFileDiffAsync(
                workspaceSlug,
                repositorySlug,
                sourceCommitHash,
                destinationCommitHash,
                filePath,
                cancellationToken);

            if (!IsCurrentFileSelection(filePath, contextVersion, detailVersion, diffVersion))
            {
                return;
            }

            _selectedFileRawDiff = string.IsNullOrWhiteSpace(diff) ? null : diff;
            CanOpenDiffDocument = _selectedFileRawDiff is not null;
            SelectedFileDiff = _selectedFileRawDiff
                ?? "El archivo no tiene diferencias textuales para mostrar.";
            DiffStatus = _selectedFileRawDiff is null
                ? $"{filePath} no tiene diferencias textuales."
                : $"Diff listo. Abrilo en el editor para verlo con formato.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentFileSelection(filePath, contextVersion, detailVersion, diffVersion))
            {
                DiffStatus = "Se canceló la carga del diff.";
            }

            throw;
        }
        catch (Exception exception)
        {
            if (IsCurrentFileSelection(filePath, contextVersion, detailVersion, diffVersion))
            {
                SelectedFileDiff = "No se pudo cargar el diff del archivo.";
                DiffStatus = $"No se pudo cargar el diff: {exception.Message}";
            }
        }
        finally
        {
            _diffLoadSemaphore.Release();
        }
    }

    private async Task OpenDiffDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        var filePath = parameter as string;
        var workspaceSlug = _workspaceSlug;
        var repositorySlug = _repositorySlug;
        var pullRequestId = SelectedPullRequestId;
        var diff = _selectedFileRawDiff;

        if (string.IsNullOrWhiteSpace(filePath) ||
            !string.Equals(filePath, SelectedChangedFilePath, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(workspaceSlug) ||
            string.IsNullOrWhiteSpace(repositorySlug) ||
            pullRequestId is null or <= 0 ||
            string.IsNullOrWhiteSpace(diff) ||
            !_changedFilesByPath.TryGetValue(filePath, out var fileChange))
        {
            DiffStatus = "Seleccioná un archivo con cambios para abrir su diff.";
            return;
        }

        CanOpenDiffDocument = false;
        DiffStatus = $"Abriendo diff de {filePath} en el editor...";
        try
        {
            var comparison = await _pullRequestChangesService.GetFileComparisonAsync(
                workspaceSlug,
                repositorySlug,
                fileChange,
                cancellationToken);
            var beforePath = comparison.Before?.Path ?? comparison.After?.Path ?? filePath;
            var afterPath = comparison.After?.Path ?? comparison.Before?.Path ?? filePath;
            await _diffDocumentService.OpenAsync(
                new PullRequestDiffDocument(
                    workspaceSlug,
                    repositorySlug,
                    pullRequestId.Value,
                    beforePath,
                    comparison.Before?.Content ?? string.Empty,
                    comparison.Before is null
                        ? "Base · archivo inexistente"
                        : $"Base · {ShortRevision(comparison.Before.Revision)}",
                    afterPath,
                    comparison.After?.Content ?? string.Empty,
                    comparison.After is null
                        ? "PR · archivo eliminado"
                        : $"PR · {ShortRevision(comparison.After.Revision)}"),
                cancellationToken);
            DiffStatus = $"Comparación de {filePath} abierta en el editor.";
        }
        catch (OperationCanceledException)
        {
            DiffStatus = "Se canceló la apertura del diff.";
        }
        catch (Exception exception)
        {
            DiffStatus = $"No se pudo abrir el diff en el editor: {exception.Message}";
        }
        finally
        {
            CanOpenDiffDocument =
                string.Equals(filePath, SelectedChangedFilePath, StringComparison.Ordinal) &&
                string.Equals(diff, _selectedFileRawDiff, StringComparison.Ordinal);
        }
    }

    private bool IsCurrentFilesLoad(long contextVersion, long detailVersion, long filesVersion) =>
        contextVersion == Volatile.Read(ref _contextVersion) &&
        detailVersion == Volatile.Read(ref _detailVersion) &&
        filesVersion == Volatile.Read(ref _filesVersion);

    private bool IsCurrentFileSelection(
        string filePath,
        long contextVersion,
        long detailVersion,
        long diffVersion) =>
        contextVersion == Volatile.Read(ref _contextVersion) &&
        detailVersion == Volatile.Read(ref _detailVersion) &&
        diffVersion == Volatile.Read(ref _diffVersion) &&
        string.Equals(SelectedChangedFilePath, filePath, StringComparison.Ordinal);

    private void ApplyDetail(BitbucketPullRequestDetail detail)
    {
        var summary = detail.Summary;
        DetailTitle = $"#{summary.Id} · {summary.Title}";
        DetailState = summary.IsDraft ? $"{summary.State} · Borrador" : summary.State;
        DetailAuthor = summary.AuthorDisplayName;
        DetailBranches = $"{summary.SourceBranch} → {summary.DestinationBranch}";
        DetailCreatedOn = FormatDate(summary.CreatedOn);
        DetailUpdatedOn = FormatDate(summary.UpdatedOn);
        DetailDescription = string.IsNullOrWhiteSpace(detail.Description)
            ? "Sin descripción."
            : detail.Description;
        DetailReviewers = detail.Reviewers.Count == 0
            ? "Sin reviewers asignados."
            : string.Join(", ", detail.Reviewers);
        DetailCounters = $"{summary.CommentCount} comentarios · {summary.TaskCount} tareas";
        DetailCommits = $"Origen: {Fallback(detail.SourceCommitHash)} · Destino: {Fallback(detail.DestinationCommitHash)}";
        DetailOptions = detail.CloseSourceBranch
            ? "La rama de origen se cerrará al mergear."
            : "La rama de origen se conservará al mergear.";
        DetailUrl = string.IsNullOrWhiteSpace(detail.HtmlUrl) ? "Sin enlace web." : detail.HtmlUrl;
        DetailDiscussionSummary = summary.CommentCount switch
        {
            0 => "Este Pull Request todavía no tiene comentarios.",
            1 => "Este Pull Request tiene 1 comentario.",
            _ => $"Este Pull Request tiene {summary.CommentCount} comentarios.",
        };
        DetailTaskSummary = summary.TaskCount switch
        {
            0 => "Este Pull Request no tiene tareas pendientes registradas.",
            1 => "Este Pull Request tiene 1 tarea registrada.",
            _ => $"Este Pull Request tiene {summary.TaskCount} tareas registradas.",
        };
        DetailActivitySummary = $"Creado: {FormatDate(summary.CreatedOn)} · Última actualización: {FormatDate(summary.UpdatedOn)}";
    }

    private void ResetPullRequests()
    {
        IsPullRequestSelectorEnabled = false;
        PullRequests.Clear();
        SetSelectedPullRequestWithoutLoading(null);
        SelectedTabIndex = 0;
        DetailTabTitle = "Detalle";
        ResetDetail();
    }

    private void ResetDetail()
    {
        Interlocked.Increment(ref _filesVersion);
        Interlocked.Increment(ref _diffVersion);
        _sourceCommitHash = null;
        _destinationCommitHash = null;
        ClearChangedFiles();
        DetailStatus = "Seleccioná un Pull Request para consultar su detalle.";
        DetailTitle = "Sin Pull Request seleccionado";
        DetailState = "—";
        DetailAuthor = "—";
        DetailBranches = "—";
        DetailCreatedOn = "—";
        DetailUpdatedOn = "—";
        DetailDescription = "—";
        DetailReviewers = "—";
        DetailCounters = "—";
        DetailCommits = "—";
        DetailOptions = "—";
        DetailUrl = "—";
        DetailDiscussionSummary = "Sin Pull Request seleccionado.";
        DetailTaskSummary = "Sin Pull Request seleccionado.";
        DetailActivitySummary = "Sin Pull Request seleccionado.";
    }

    private void ClearChangedFiles()
    {
        IsChangedFileSelectorEnabled = false;
        ChangedFiles.Clear();
        _changedFilesByPath.Clear();
        SetSelectedChangedFileWithoutLoading(null);
        FilesStatus = "Seleccioná un Pull Request para cargar sus archivos modificados.";
        ResetDiff();
    }

    private void ResetDiff()
    {
        _selectedFileRawDiff = null;
        CanOpenDiffDocument = false;
        SelectedFileTitle = "Diff";
        SelectedFileDiff = "Seleccioná un archivo para visualizar sus cambios.";
        DiffStatus = "Seleccioná un archivo modificado para cargar su diff.";
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

    private void SetSelectedChangedFileWithoutLoading(string? filePath)
    {
        _suppressChangedFileLoad = true;
        try
        {
            SelectedChangedFilePath = filePath;
        }
        finally
        {
            _suppressChangedFileLoad = false;
        }
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Sin fecha informada";

    private static string Fallback(string value) =>
        string.IsNullOrWhiteSpace(value) ? "sin hash" : value;

    private static string ShortRevision(string revision) =>
        revision.Length <= 8 ? revision : revision[..8];
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

[DataContract]
internal sealed class PullRequestFileListItem
{
    public PullRequestFileListItem(BitbucketPullRequestFileChange file)
    {
        Path = file.Path;
        FileName = System.IO.Path.GetFileName(file.Path);
        Directory = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/') ?? "/";
        Change = TranslateStatus(file.Status);
        LineSummary = $"+{file.LinesAdded}  -{file.LinesRemoved}";
        PreviousPath = string.IsNullOrWhiteSpace(file.PreviousPath)
            ? string.Empty
            : $"Antes: {file.PreviousPath}";
    }

    [DataMember]
    public string Path { get; }

    [DataMember]
    public string FileName { get; }

    [DataMember]
    public string Directory { get; }

    [DataMember]
    public string Change { get; }

    [DataMember]
    public string LineSummary { get; }

    [DataMember]
    public string PreviousPath { get; }

    private static string TranslateStatus(string status) => status.ToLowerInvariant() switch
    {
        "added" => "Agregado",
        "removed" => "Eliminado",
        "renamed" => "Renombrado",
        "modified" => "Modificado",
        _ => status,
    };
}
