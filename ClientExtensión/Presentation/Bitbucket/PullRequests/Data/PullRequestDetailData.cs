using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Data;

[DataContract]
internal sealed class PullRequestDetailData : NotifyPropertyChangedObject
{
    private readonly IBitbucketPullRequestService _pullRequestService;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    private string _status = "Seleccioná un Pull Request para consultar su detalle.";
    private string _title = "Sin Pull Request seleccionado";
    private string _state = "—";
    private string _author = "—";
    private string _branches = "—";
    private string _createdOn = "—";
    private string _updatedOn = "—";
    private string _description = "—";
    private string _reviewers = "—";
    private string _commits = "—";
    private string _options = "—";
    private string _url = "—";
    private string _taskSummary = "Sin Pull Request seleccionado.";
    private string _activitySummary = "Sin Pull Request seleccionado.";
    private long _loadVersion;

    public PullRequestDetailData(IBitbucketPullRequestService pullRequestService)
    {
        _pullRequestService = pullRequestService;
    }

    [DataMember]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [DataMember]
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    [DataMember]
    public string State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    [DataMember]
    public string Author
    {
        get => _author;
        private set => SetProperty(ref _author, value);
    }

    [DataMember]
    public string Branches
    {
        get => _branches;
        private set => SetProperty(ref _branches, value);
    }

    [DataMember]
    public string CreatedOn
    {
        get => _createdOn;
        private set => SetProperty(ref _createdOn, value);
    }

    [DataMember]
    public string UpdatedOn
    {
        get => _updatedOn;
        private set => SetProperty(ref _updatedOn, value);
    }

    [DataMember]
    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    [DataMember]
    public string Reviewers
    {
        get => _reviewers;
        private set => SetProperty(ref _reviewers, value);
    }

    [DataMember]
    public string Commits
    {
        get => _commits;
        private set => SetProperty(ref _commits, value);
    }

    [DataMember]
    public string Options
    {
        get => _options;
        private set => SetProperty(ref _options, value);
    }

    [DataMember]
    public string Url
    {
        get => _url;
        private set => SetProperty(ref _url, value);
    }

    [DataMember]
    public string TaskSummary
    {
        get => _taskSummary;
        private set => SetProperty(ref _taskSummary, value);
    }

    [DataMember]
    public string ActivitySummary
    {
        get => _activitySummary;
        private set => SetProperty(ref _activitySummary, value);
    }

    internal async Task<BitbucketPullRequestDetail?> LoadAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken)
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        Status = $"Cargando detalle del Pull Request #{pullRequestId}...";
        var lockTaken = false;

        try
        {
            await _loadSemaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            if (!IsCurrent(loadVersion))
            {
                return null;
            }

            var detail = await _pullRequestService.GetByIdAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                cancellationToken);
            if (!IsCurrent(loadVersion))
            {
                return null;
            }

            Apply(detail);
            Status = $"Detalle del Pull Request #{pullRequestId}.";
            return detail;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(loadVersion))
            {
                Status = "Se canceló la carga del detalle del Pull Request.";
            }

            return null;
        }
        catch (Exception exception)
        {
            if (IsCurrent(loadVersion))
            {
                ResetContent();
                Status = $"No se pudo cargar el detalle del Pull Request: {exception.Message}";
            }

            return null;
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
        ResetContent();
    }

    private bool IsCurrent(long loadVersion) =>
        loadVersion == Volatile.Read(ref _loadVersion);

    private void Apply(BitbucketPullRequestDetail detail)
    {
        var summary = detail.Summary;
        Title = $"#{summary.Id} · {summary.Title}";
        State = summary.IsDraft ? $"{summary.State} · Borrador" : summary.State;
        Author = summary.AuthorDisplayName;
        Branches = $"{summary.SourceBranch} → {summary.DestinationBranch}";
        CreatedOn = FormatDate(summary.CreatedOn);
        UpdatedOn = FormatDate(summary.UpdatedOn);
        Description = string.IsNullOrWhiteSpace(detail.Description)
            ? "Sin descripción."
            : detail.Description;
        Reviewers = detail.Reviewers.Count == 0
            ? "Sin reviewers asignados."
            : string.Join(", ", detail.Reviewers);
        Commits = $"Origen: {Fallback(detail.SourceCommitHash)} · Destino: {Fallback(detail.DestinationCommitHash)}";
        Options = detail.CloseSourceBranch
            ? "La rama de origen se cerrará al mergear."
            : "La rama de origen se conservará al mergear.";
        Url = string.IsNullOrWhiteSpace(detail.HtmlUrl) ? "Sin enlace web." : detail.HtmlUrl;
        TaskSummary = summary.TaskCount switch
        {
            0 => "Este Pull Request no tiene tareas pendientes registradas.",
            1 => "Este Pull Request tiene 1 tarea registrada.",
            _ => $"Este Pull Request tiene {summary.TaskCount} tareas registradas.",
        };
        ActivitySummary = $"Creado: {FormatDate(summary.CreatedOn)} · Última actualización: {FormatDate(summary.UpdatedOn)}";
    }

    private void ResetContent()
    {
        Status = "Seleccioná un Pull Request para consultar su detalle.";
        Title = "Sin Pull Request seleccionado";
        State = "—";
        Author = "—";
        Branches = "—";
        CreatedOn = "—";
        UpdatedOn = "—";
        Description = "—";
        Reviewers = "—";
        Commits = "—";
        Options = "—";
        Url = "—";
        TaskSummary = "Sin Pull Request seleccionado.";
        ActivitySummary = "Sin Pull Request seleccionado.";
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Sin fecha informada";

    private static string Fallback(string value) =>
        string.IsNullOrWhiteSpace(value) ? "sin hash" : value;
}
