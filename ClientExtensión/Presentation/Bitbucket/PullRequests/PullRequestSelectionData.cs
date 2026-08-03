using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

[DataContract]
internal sealed class PullRequestSelectionData : NotifyPropertyChangedObject
{
    private readonly IBitbucketWorkspaceService _workspaceService;
    private readonly IBitbucketWorkspaceSelectionStore _workspaceSelectionStore;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private string? _selectedWorkspaceSlug;
    private string _status = "Conectá Bitbucket para cargar tus workspaces.";
    private bool _isWorkspaceSelectorEnabled;
    private bool _isLoaded;
    private bool _suppressSelectionPersistence;
    private long _selectionVersion;

    public PullRequestSelectionData(
        IBitbucketWorkspaceService workspaceService,
        IBitbucketWorkspaceSelectionStore workspaceSelectionStore)
    {
        _workspaceService = workspaceService;
        _workspaceSelectionStore = workspaceSelectionStore;
    }

    [DataMember]
    public string Title => "Pull Requests de Bitbucket";

    [DataMember]
    public string Subtitle => "Elegí el workspace donde querés consultar y revisar Pull Requests.";

    [DataMember]
    public string WorkspaceSectionTitle => "Workspace";

    [DataMember]
    public string WorkspaceDescription =>
        "El workspace seleccionado se usará como contexto para las próximas consultas a Bitbucket.";

    [DataMember]
    public ObservableCollection<WorkspaceOption> Workspaces { get; } = [];

    [DataMember]
    public string? SelectedWorkspaceSlug
    {
        get => _selectedWorkspaceSlug;
        set
        {
            if (!SetProperty(ref _selectedWorkspaceSlug, value) || _suppressSelectionPersistence)
            {
                return;
            }

            QueueWorkspaceSelectionSave(value);
        }
    }

    [DataMember]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [DataMember]
    public bool IsWorkspaceSelectorEnabled
    {
        get => _isWorkspaceSelectorEnabled;
        private set => SetProperty(ref _isWorkspaceSelectorEnabled, value);
    }

    [DataMember]
    public string NextStep => "Después de elegir un workspace podrás seleccionar el Pull Request a revisar.";

    public void InvalidateWorkspaces()
    {
        Interlocked.Increment(ref _selectionVersion);
        _isLoaded = false;
        IsWorkspaceSelectorEnabled = false;
        Workspaces.Clear();
        SetSelectedWorkspaceWithoutSaving(null);
        Status = "Cargando workspaces de Bitbucket...";
    }

    public void ClearWorkspaces()
    {
        InvalidateWorkspaces();
        Status = "Conectá Bitbucket para cargar tus workspaces.";
    }

    public async Task EnsureWorkspacesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return;
        }

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isLoaded)
            {
                return;
            }

            IsWorkspaceSelectorEnabled = false;
            Status = "Cargando workspaces de Bitbucket...";

            var workspaces = await _workspaceService.GetAllAsync(cancellationToken);
            var savedWorkspaceSlug = await _workspaceSelectionStore.GetAsync(cancellationToken);

            Workspaces.Clear();
            foreach (var workspace in workspaces)
            {
                Workspaces.Add(new WorkspaceOption(workspace.Slug));
            }

            var selectedWorkspaceSlug = workspaces
                .FirstOrDefault(workspace => string.Equals(
                    workspace.Slug,
                    savedWorkspaceSlug,
                    StringComparison.OrdinalIgnoreCase))
                ?.Slug;

            if (savedWorkspaceSlug is not null && selectedWorkspaceSlug is null)
            {
                await _workspaceSelectionStore.ClearAsync(cancellationToken);
            }

            SetSelectedWorkspaceWithoutSaving(selectedWorkspaceSlug);
            IsWorkspaceSelectorEnabled = workspaces.Count > 0;
            Status = workspaces.Count switch
            {
                0 => "No hay workspaces disponibles para el usuario autenticado.",
                _ when selectedWorkspaceSlug is not null => $"Workspace seleccionado: {selectedWorkspaceSlug}.",
                _ => "Seleccioná un workspace. El cambio se guardará automáticamente.",
            };
            _isLoaded = true;
        }
        catch (OperationCanceledException)
        {
            Status = "Se canceló la carga de workspaces de Bitbucket.";
            throw;
        }
        catch (Exception exception)
        {
            Status = $"No se pudieron cargar los workspaces de Bitbucket: {exception.Message}";
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    private void QueueWorkspaceSelectionSave(string? workspaceSlug)
    {
        var version = Interlocked.Increment(ref _selectionVersion);
        _ = PersistWorkspaceSelectionAsync(workspaceSlug, version);
    }

    private async Task PersistWorkspaceSelectionAsync(string? workspaceSlug, long version)
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref _selectionVersion))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(workspaceSlug))
            {
                await _workspaceSelectionStore.ClearAsync(CancellationToken.None);
                Status = "Seleccioná un workspace. El cambio se guardará automáticamente.";
            }
            else
            {
                await _workspaceSelectionStore.SaveAsync(workspaceSlug, CancellationToken.None);
                Status = $"Workspace seleccionado: {workspaceSlug}.";
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _selectionVersion))
            {
                Status = $"No se pudo guardar el workspace seleccionado: {exception.Message}";
            }
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private void SetSelectedWorkspaceWithoutSaving(string? workspaceSlug)
    {
        _suppressSelectionPersistence = true;
        try
        {
            SelectedWorkspaceSlug = workspaceSlug;
        }
        finally
        {
            _suppressSelectionPersistence = false;
        }
    }
}

[DataContract]
internal sealed class WorkspaceOption
{
    public WorkspaceOption(string slug)
    {
        Slug = slug;
    }

    [DataMember]
    public string Slug { get; }
}
