using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.IntegrationHub;

[DataContract]
internal sealed class IntegrationHubData : NotifyPropertyChangedObject
{
    private readonly IBitbucketWorkspaceService _workspaceService;
    private readonly IBitbucketWorkspaceSelectionStore _workspaceSelectionStore;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private string? _selectedWorkspaceSlug;
    private string _bitbucketStatus = "Conectá Bitbucket para cargar tus workspaces.";
    private bool _isBitbucketWorkspaceSelectorEnabled;
    private bool _isLoaded;
    private bool _suppressSelectionPersistence;
    private long _selectionVersion;

    public IntegrationHubData(
        IBitbucketWorkspaceService workspaceService,
        IBitbucketWorkspaceSelectionStore workspaceSelectionStore)
    {
        _workspaceService = workspaceService;
        _workspaceSelectionStore = workspaceSelectionStore;
    }

    [DataMember]
    public string Title => "Centro de integraciones";

    [DataMember]
    public string Subtitle => "Un único punto de trabajo para Bitbucket y Jira.";

    [DataMember]
    public string BitbucketTitle => "Bitbucket";

    [DataMember]
    public string BitbucketDescription => "Pull requests, revisiones y comentarios del repositorio activo.";

    [DataMember]
    public ObservableCollection<BitbucketWorkspaceOption> BitbucketWorkspaces { get; } = [];

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
    public string BitbucketStatus
    {
        get => _bitbucketStatus;
        private set => SetProperty(ref _bitbucketStatus, value);
    }

    [DataMember]
    public bool IsBitbucketWorkspaceSelectorEnabled
    {
        get => _isBitbucketWorkspaceSelectorEnabled;
        private set => SetProperty(ref _isBitbucketWorkspaceSelectorEnabled, value);
    }

    [DataMember]
    public string JiraTitle => "Jira";

    [DataMember]
    public string JiraDescription => "Historias de usuario, tareas y trazabilidad asociada a la rama actual.";

    [DataMember]
    public string JiraStatus => "Próximamente: conectar datos de incidencias.";

    [DataMember]
    public string NextStep => "Seleccioná un workspace para continuar con la integración de Bitbucket.";

    public void InvalidateWorkspaces()
    {
        Interlocked.Increment(ref _selectionVersion);
        _isLoaded = false;
        IsBitbucketWorkspaceSelectorEnabled = false;
        BitbucketWorkspaces.Clear();
        SetSelectedWorkspaceWithoutSaving(null);
        BitbucketStatus = "Cargando workspaces de Bitbucket...";
    }

    public void ClearWorkspaces()
    {
        InvalidateWorkspaces();
        BitbucketStatus = "Conectá Bitbucket para cargar tus workspaces.";
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

            IsBitbucketWorkspaceSelectorEnabled = false;
            BitbucketStatus = "Cargando workspaces de Bitbucket...";

            var workspaces = await _workspaceService.GetAllAsync(cancellationToken);
            var savedWorkspaceSlug = await _workspaceSelectionStore.GetAsync(cancellationToken);

            BitbucketWorkspaces.Clear();
            foreach (var workspace in workspaces)
            {
                BitbucketWorkspaces.Add(new BitbucketWorkspaceOption(workspace.Slug));
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
            IsBitbucketWorkspaceSelectorEnabled = workspaces.Count > 0;
            BitbucketStatus = workspaces.Count switch
            {
                0 => "No hay workspaces disponibles para el usuario autenticado.",
                _ when selectedWorkspaceSlug is not null => $"Workspace seleccionado: {selectedWorkspaceSlug}.",
                _ => "Seleccioná un workspace. El cambio se guardará automáticamente.",
            };
            _isLoaded = true;
        }
        catch (OperationCanceledException)
        {
            BitbucketStatus = "Se canceló la carga de workspaces de Bitbucket.";
            throw;
        }
        catch (Exception exception)
        {
            BitbucketStatus = $"No se pudieron cargar los workspaces de Bitbucket: {exception.Message}";
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
                BitbucketStatus = "Seleccioná un workspace. El cambio se guardará automáticamente.";
            }
            else
            {
                await _workspaceSelectionStore.SaveAsync(workspaceSlug, CancellationToken.None);
                BitbucketStatus = $"Workspace seleccionado: {workspaceSlug}.";
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _selectionVersion))
            {
                BitbucketStatus = $"No se pudo guardar el workspace seleccionado: {exception.Message}";
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
internal sealed class BitbucketWorkspaceOption
{
    public BitbucketWorkspaceOption(string slug)
    {
        Slug = slug;
    }

    [DataMember]
    public string Slug { get; }
}
