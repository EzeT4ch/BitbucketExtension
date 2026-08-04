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
    private readonly IBitbucketRepositoryService _repositoryService;
    private readonly IBitbucketRepositorySelectionStore _repositorySelectionStore;
    private readonly PullRequestBrowserData _pullRequestBrowser;
    private readonly SemaphoreSlim _workspaceLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _repositoryLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _workspaceSaveSemaphore = new(1, 1);
    private readonly SemaphoreSlim _repositorySaveSemaphore = new(1, 1);

    private string? _selectedWorkspaceSlug;
    private string? _selectedRepositorySlug;
    private string _workspaceStatus = "Conectá Bitbucket para cargar tus workspaces.";
    private string _repositoryStatus = "Seleccioná un workspace para cargar sus repositorios.";
    private bool _isWorkspaceSelectorEnabled;
    private bool _isRepositorySelectorEnabled;
    private bool _areWorkspacesLoaded;
    private bool _suppressWorkspaceSelectionChange;
    private bool _suppressRepositorySelectionPersistence;
    private long _workspaceSelectionVersion;
    private long _repositoryContextVersion;
    private long _repositorySelectionVersion;

    public PullRequestSelectionData(
        IBitbucketWorkspaceService workspaceService,
        IBitbucketWorkspaceSelectionStore workspaceSelectionStore,
        IBitbucketRepositoryService repositoryService,
        IBitbucketRepositorySelectionStore repositorySelectionStore,
        PullRequestBrowserData pullRequestBrowser)
    {
        _workspaceService = workspaceService;
        _workspaceSelectionStore = workspaceSelectionStore;
        _repositoryService = repositoryService;
        _repositorySelectionStore = repositorySelectionStore;
        _pullRequestBrowser = pullRequestBrowser;
    }

    [DataMember]
    public string Title => "Pull Requests de Bitbucket";

    [DataMember]
    public string Subtitle => "Elegí el workspace y el repositorio donde querés consultar Pull Requests.";

    [DataMember]
    public string WorkspaceSectionTitle => "Workspace";

    [DataMember]
    public string WorkspaceDescription =>
        "El workspace seleccionado define dónde se buscarán los repositorios disponibles.";

    [DataMember]
    public string RepositorySectionTitle => "Repositorio";

    [DataMember]
    public string RepositoryDescription =>
        "El repositorio seleccionado se usará como contexto para consultar los Pull Requests activos.";

    [DataMember]
    public ObservableCollection<WorkspaceOption> Workspaces { get; } = [];

    [DataMember]
    public ObservableCollection<RepositoryOption> Repositories { get; } = [];

    [DataMember]
    public PullRequestBrowserData PullRequestBrowser => _pullRequestBrowser;

    [DataMember]
    public string? SelectedWorkspaceSlug
    {
        get => _selectedWorkspaceSlug;
        set
        {
            if (!SetProperty(ref _selectedWorkspaceSlug, value) || _suppressWorkspaceSelectionChange)
            {
                return;
            }

            QueueWorkspaceSelectionSave(value);
            ReloadRepositories(value);
        }
    }

    [DataMember]
    public string? SelectedRepositorySlug
    {
        get => _selectedRepositorySlug;
        set
        {
            if (!SetProperty(ref _selectedRepositorySlug, value) || _suppressRepositorySelectionPersistence)
            {
                return;
            }

            QueueRepositorySelectionSave(_selectedWorkspaceSlug, value);
            _pullRequestBrowser.SetContext(_selectedWorkspaceSlug, value);
        }
    }

    [DataMember]
    public string WorkspaceStatus
    {
        get => _workspaceStatus;
        private set => SetProperty(ref _workspaceStatus, value);
    }

    [DataMember]
    public string RepositoryStatus
    {
        get => _repositoryStatus;
        private set => SetProperty(ref _repositoryStatus, value);
    }

    [DataMember]
    public bool IsWorkspaceSelectorEnabled
    {
        get => _isWorkspaceSelectorEnabled;
        private set => SetProperty(ref _isWorkspaceSelectorEnabled, value);
    }

    [DataMember]
    public bool IsRepositorySelectorEnabled
    {
        get => _isRepositorySelectorEnabled;
        private set => SetProperty(ref _isRepositorySelectorEnabled, value);
    }

    [DataMember]
    public string NextStep => "Los Pull Requests activos se cargan automáticamente al elegir un repositorio.";

    public void InvalidateWorkspaces()
    {
        Interlocked.Increment(ref _workspaceSelectionVersion);
        Interlocked.Increment(ref _repositoryContextVersion);
        Interlocked.Increment(ref _repositorySelectionVersion);
        _areWorkspacesLoaded = false;
        IsWorkspaceSelectorEnabled = false;
        Workspaces.Clear();
        SetSelectedWorkspaceWithoutSideEffects(null);
        ClearRepositories("Seleccioná un workspace para cargar sus repositorios.");
        WorkspaceStatus = "Cargando workspaces de Bitbucket...";
    }

    public void ClearWorkspaces()
    {
        InvalidateWorkspaces();
        WorkspaceStatus = "Conectá Bitbucket para cargar tus workspaces.";
    }

    public async Task EnsureWorkspacesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_areWorkspacesLoaded)
        {
            return;
        }

        await _workspaceLoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_areWorkspacesLoaded)
            {
                return;
            }

            IsWorkspaceSelectorEnabled = false;
            WorkspaceStatus = "Cargando workspaces de Bitbucket...";

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

            SetSelectedWorkspaceWithoutSideEffects(selectedWorkspaceSlug);
            IsWorkspaceSelectorEnabled = workspaces.Count > 0;
            WorkspaceStatus = workspaces.Count switch
            {
                0 => "No hay workspaces disponibles para el usuario autenticado.",
                _ when selectedWorkspaceSlug is not null => $"Workspace seleccionado: {selectedWorkspaceSlug}.",
                _ => "Seleccioná un workspace. El cambio se guardará automáticamente.",
            };
            _areWorkspacesLoaded = true;

            if (selectedWorkspaceSlug is null)
            {
                ClearRepositories("Seleccioná un workspace para cargar sus repositorios.");
            }
            else
            {
                var contextVersion = PrepareRepositoryReload();
                await LoadRepositoriesAsync(selectedWorkspaceSlug, contextVersion, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _areWorkspacesLoaded = false;
            WorkspaceStatus = "Se canceló la carga de workspaces de Bitbucket.";
            throw;
        }
        catch (Exception exception)
        {
            WorkspaceStatus = $"No se pudieron cargar los workspaces de Bitbucket: {exception.Message}";
        }
        finally
        {
            _workspaceLoadSemaphore.Release();
        }
    }

    private void ReloadRepositories(string? workspaceSlug)
    {
        var contextVersion = PrepareRepositoryReload();
        if (string.IsNullOrWhiteSpace(workspaceSlug))
        {
            RepositoryStatus = "Seleccioná un workspace para cargar sus repositorios.";
            return;
        }

        _ = LoadRepositoriesAsync(workspaceSlug, contextVersion, CancellationToken.None);
    }

    private long PrepareRepositoryReload()
    {
        var contextVersion = Interlocked.Increment(ref _repositoryContextVersion);
        Interlocked.Increment(ref _repositorySelectionVersion);
        ClearRepositories("Cargando repositorios de Bitbucket...");
        return contextVersion;
    }

    private async Task LoadRepositoriesAsync(
        string workspaceSlug,
        long contextVersion,
        CancellationToken cancellationToken)
    {
        await _repositoryLoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (contextVersion != Volatile.Read(ref _repositoryContextVersion))
            {
                return;
            }

            var repositories = await _repositoryService.GetAllAsync(workspaceSlug, cancellationToken);
            var savedRepositorySlug = await _repositorySelectionStore.GetAsync(workspaceSlug, cancellationToken);

            if (contextVersion != Volatile.Read(ref _repositoryContextVersion))
            {
                return;
            }

            Repositories.Clear();
            foreach (var repository in repositories)
            {
                Repositories.Add(new RepositoryOption(repository.Name, repository.Slug));
            }

            var selectedRepositorySlug = repositories
                .FirstOrDefault(repository => string.Equals(
                    repository.Slug,
                    savedRepositorySlug,
                    StringComparison.OrdinalIgnoreCase))
                ?.Slug;

            if (savedRepositorySlug is not null && selectedRepositorySlug is null)
            {
                await _repositorySelectionStore.ClearAsync(cancellationToken);
            }

            if (contextVersion != Volatile.Read(ref _repositoryContextVersion))
            {
                return;
            }

            SetSelectedRepositoryWithoutSaving(selectedRepositorySlug);
            _pullRequestBrowser.SetContext(workspaceSlug, selectedRepositorySlug);
            IsRepositorySelectorEnabled = repositories.Count > 0;
            RepositoryStatus = repositories.Count switch
            {
                0 => $"No hay repositorios disponibles en el workspace {workspaceSlug}.",
                _ when selectedRepositorySlug is not null => $"Repositorio seleccionado: {selectedRepositorySlug}.",
                _ => "Seleccioná un repositorio. El cambio se guardará automáticamente.",
            };
        }
        catch (OperationCanceledException)
        {
            if (contextVersion == Volatile.Read(ref _repositoryContextVersion))
            {
                RepositoryStatus = "Se canceló la carga de repositorios de Bitbucket.";
            }

            throw;
        }
        catch (Exception exception)
        {
            if (contextVersion == Volatile.Read(ref _repositoryContextVersion))
            {
                ClearRepositories($"No se pudieron cargar los repositorios de Bitbucket: {exception.Message}");
            }
        }
        finally
        {
            _repositoryLoadSemaphore.Release();
        }
    }

    private void QueueWorkspaceSelectionSave(string? workspaceSlug)
    {
        var version = Interlocked.Increment(ref _workspaceSelectionVersion);
        _ = PersistWorkspaceSelectionAsync(workspaceSlug, version);
    }

    private async Task PersistWorkspaceSelectionAsync(string? workspaceSlug, long version)
    {
        await _workspaceSaveSemaphore.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref _workspaceSelectionVersion))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(workspaceSlug))
            {
                await _workspaceSelectionStore.ClearAsync(CancellationToken.None);
                WorkspaceStatus = "Seleccioná un workspace. El cambio se guardará automáticamente.";
            }
            else
            {
                await _workspaceSelectionStore.SaveAsync(workspaceSlug, CancellationToken.None);
                WorkspaceStatus = $"Workspace seleccionado: {workspaceSlug}.";
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _workspaceSelectionVersion))
            {
                WorkspaceStatus = $"No se pudo guardar el workspace seleccionado: {exception.Message}";
            }
        }
        finally
        {
            _workspaceSaveSemaphore.Release();
        }
    }

    private void QueueRepositorySelectionSave(string? workspaceSlug, string? repositorySlug)
    {
        var version = Interlocked.Increment(ref _repositorySelectionVersion);
        _ = PersistRepositorySelectionAsync(workspaceSlug, repositorySlug, version);
    }

    private async Task PersistRepositorySelectionAsync(
        string? workspaceSlug,
        string? repositorySlug,
        long version)
    {
        await _repositorySaveSemaphore.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref _repositorySelectionVersion) ||
                !string.Equals(workspaceSlug, _selectedWorkspaceSlug, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(workspaceSlug) || string.IsNullOrWhiteSpace(repositorySlug))
            {
                await _repositorySelectionStore.ClearAsync(CancellationToken.None);
                RepositoryStatus = "Seleccioná un repositorio. El cambio se guardará automáticamente.";
            }
            else
            {
                await _repositorySelectionStore.SaveAsync(
                    workspaceSlug,
                    repositorySlug,
                    CancellationToken.None);
                RepositoryStatus = $"Repositorio seleccionado: {repositorySlug}.";
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _repositorySelectionVersion))
            {
                RepositoryStatus = $"No se pudo guardar el repositorio seleccionado: {exception.Message}";
            }
        }
        finally
        {
            _repositorySaveSemaphore.Release();
        }
    }

    private void ClearRepositories(string status)
    {
        IsRepositorySelectorEnabled = false;
        Repositories.Clear();
        SetSelectedRepositoryWithoutSaving(null);
        _pullRequestBrowser.ClearContext();
        RepositoryStatus = status;
    }

    private void SetSelectedWorkspaceWithoutSideEffects(string? workspaceSlug)
    {
        _suppressWorkspaceSelectionChange = true;
        try
        {
            SelectedWorkspaceSlug = workspaceSlug;
        }
        finally
        {
            _suppressWorkspaceSelectionChange = false;
        }
    }

    private void SetSelectedRepositoryWithoutSaving(string? repositorySlug)
    {
        _suppressRepositorySelectionPersistence = true;
        try
        {
            SelectedRepositorySlug = repositorySlug;
        }
        finally
        {
            _suppressRepositorySelectionPersistence = false;
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

[DataContract]
internal sealed class RepositoryOption
{
    public RepositoryOption(string name, string slug)
    {
        Name = name;
        Slug = slug;
        DisplayName = string.Equals(name, slug, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} ({slug})";
    }

    [DataMember]
    public string Name { get; }

    [DataMember]
    public string Slug { get; }

    [DataMember]
    public string DisplayName { get; }
}
