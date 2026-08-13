using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Application.Abstractions;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Data;

[DataContract]
internal sealed class PullRequestFilesData : NotifyPropertyChangedObject
{
    private readonly IBitbucketPullRequestChangesService _pullRequestChangesService;
    private readonly IBitbucketPullRequestCommentService _pullRequestCommentService;
    private readonly IPullRequestDiffDocumentService _diffDocumentService;
    private readonly Dictionary<string, BitbucketPullRequestFileChange> _changedFilesByPath =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _filesLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _diffLoadSemaphore = new(1, 1);

    private IPullRequestReplyTarget? _activeDiffReplyRow;
    private string? _workspaceSlug;
    private string? _repositorySlug;
    private long? _pullRequestId;
    private string? _sourceCommitHash;
    private string? _destinationCommitHash;
    private string? _selectedPath;
    private string _status = "Seleccioná un Pull Request para cargar sus archivos modificados.";
    private string _diffStatus = "Seleccioná un archivo modificado para cargar su diff.";
    private string _selectedFileTitle = "Diff";
    private string? _selectedFileRawDiff;
    private bool _isSelectorEnabled;
    private bool _canOpenDiffDocument;
    private bool _suppressSelectionLoad;
    private long _loadVersion;
    private long _diffVersion;

    public PullRequestFilesData(
        IBitbucketPullRequestChangesService pullRequestChangesService,
        IBitbucketPullRequestCommentService pullRequestCommentService,
        IPullRequestDiffDocumentService diffDocumentService)
    {
        _pullRequestChangesService = pullRequestChangesService;
        _pullRequestCommentService = pullRequestCommentService;
        _diffDocumentService = diffDocumentService;
        OpenDiffDocumentCommand = new AsyncCommand(OpenDiffDocumentAsync);
        SetDiffPlaceholder("Seleccioná un archivo para visualizar sus cambios.");
    }

    [DataMember]
    public ObservableCollection<PullRequestFileTreeNode> Tree { get; } = [];

    [DataMember]
    public ObservableCollection<PullRequestDiffRow> DiffRows { get; } = [];

    [DataMember]
    public string? SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (!SetProperty(ref _selectedPath, value) || _suppressSelectionLoad)
            {
                return;
            }

            LoadSelectedFileDiff(value);
        }
    }

    [DataMember]
    public bool IsSelectorEnabled
    {
        get => _isSelectorEnabled;
        private set => SetProperty(ref _isSelectorEnabled, value);
    }

    [DataMember]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
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
    public bool CanOpenDiffDocument
    {
        get => _canOpenDiffDocument;
        private set => SetProperty(ref _canOpenDiffDocument, value);
    }

    [DataMember]
    public AsyncCommand OpenDiffDocumentCommand { get; }

    internal async Task LoadAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        BitbucketPullRequestDetail detail,
        CancellationToken cancellationToken)
    {
        _workspaceSlug = workspaceSlug;
        _repositorySlug = repositorySlug;
        _pullRequestId = pullRequestId;
        _sourceCommitHash = detail.SourceCommitHash;
        _destinationCommitHash = detail.DestinationCommitHash;
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        Interlocked.Increment(ref _diffVersion);
        ClearFiles();

        if (string.IsNullOrWhiteSpace(_sourceCommitHash) ||
            string.IsNullOrWhiteSpace(_destinationCommitHash))
        {
            Status = "Bitbucket no informó los commits necesarios para consultar los archivos modificados.";
            return;
        }

        Status = "Cargando archivos modificados...";
        var lockTaken = false;
        try
        {
            await _filesLoadSemaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            if (!IsCurrentLoad(loadVersion))
            {
                return;
            }

            var files = await _pullRequestChangesService.GetFilesAsync(
                workspaceSlug,
                repositorySlug,
                _sourceCommitHash,
                _destinationCommitHash,
                cancellationToken);
            if (!IsCurrentLoad(loadVersion))
            {
                return;
            }

            _changedFilesByPath.Clear();
            foreach (var file in files)
            {
                _changedFilesByPath[file.Path] = file;
            }

            BuildTree(files);
            IsSelectorEnabled = files.Count > 0;
            Status = files.Count switch
            {
                0 => "El Pull Request no tiene archivos modificados.",
                1 => "Se encontró 1 archivo modificado.",
                _ => $"Se encontraron {files.Count} archivos modificados.",
            };
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentLoad(loadVersion))
            {
                Status = "Se canceló la carga de archivos modificados.";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentLoad(loadVersion))
            {
                ClearFiles();
                Status = $"No se pudieron cargar los archivos modificados: {exception.Message}";
            }
        }
        finally
        {
            if (lockTaken)
            {
                _filesLoadSemaphore.Release();
            }
        }
    }

    internal void Reset()
    {
        Interlocked.Increment(ref _loadVersion);
        Interlocked.Increment(ref _diffVersion);
        _workspaceSlug = null;
        _repositorySlug = null;
        _pullRequestId = null;
        _sourceCommitHash = null;
        _destinationCommitHash = null;
        ClearFiles();
    }

    private void BuildTree(IReadOnlyList<BitbucketPullRequestFileChange> files)
    {
        var roots = PullRequestFileTreeBuilder.Build(
            files,
            filePath => SelectedPath = filePath);
        Tree.Clear();
        foreach (var root in roots)
        {
            Tree.Add(root);
        }
    }

    private void LoadSelectedFileDiff(string? filePath)
    {
        var diffVersion = Interlocked.Increment(ref _diffVersion);
        ResetDiff();

        if (string.IsNullOrWhiteSpace(filePath) ||
            string.IsNullOrWhiteSpace(_workspaceSlug) ||
            string.IsNullOrWhiteSpace(_repositorySlug) ||
            _pullRequestId is null or <= 0 ||
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
            _pullRequestId.Value,
            _sourceCommitHash,
            _destinationCommitHash,
            filePath,
            Volatile.Read(ref _loadVersion),
            diffVersion,
            CancellationToken.None);
    }

    private async Task LoadFileDiffAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string sourceCommitHash,
        string destinationCommitHash,
        string filePath,
        long loadVersion,
        long diffVersion,
        CancellationToken cancellationToken)
    {
        var lockTaken = false;
        try
        {
            await _diffLoadSemaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            if (!IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                return;
            }

            var diffTask = _pullRequestChangesService.GetFileDiffAsync(
                workspaceSlug,
                repositorySlug,
                sourceCommitHash,
                destinationCommitHash,
                filePath,
                cancellationToken);
            var commentsTask = GetFileCommentsSafelyAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                filePath,
                cancellationToken);

            var diff = await diffTask;
            var commentsResult = await commentsTask;
            if (!IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                return;
            }

            _selectedFileRawDiff = string.IsNullOrWhiteSpace(diff) ? null : diff;
            CanOpenDiffDocument = _selectedFileRawDiff is not null;
            DiffRows.Clear();
            if (_selectedFileRawDiff is null)
            {
                SetDiffPlaceholder("El archivo no tiene diferencias textuales para mostrar.");
                DiffStatus = $"{filePath} no tiene diferencias textuales.";
                return;
            }

            var rows = PullRequestUnifiedDiffBuilder.Build(
                _selectedFileRawDiff,
                commentsResult.Comments,
                filePath);
            await LocalizeCommentImagesAsync(
                rows,
                workspaceSlug,
                repositorySlug,
                cancellationToken);
            if (!IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                return;
            }

            foreach (var row in rows)
            {
                row.ConfigureReply(OpenDiffReply, CancelDiffReply, SendDiffReplyAsync);
                foreach (var reply in row.Replies)
                {
                    reply.ConfigureReply(OpenDiffReply, CancelDiffReply, SendDiffReplyAsync);
                }

                DiffRows.Add(row);
            }

            DiffStatus = BuildDiffStatus(commentsResult.Comments.Count, commentsResult.ErrorMessage);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                DiffStatus = "Se canceló la carga del diff.";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                SetDiffPlaceholder("No se pudo cargar el diff del archivo.");
                DiffStatus = $"No se pudo cargar el diff: {exception.Message}";
            }
        }
        finally
        {
            if (lockTaken)
            {
                _diffLoadSemaphore.Release();
            }
        }
    }

    private async Task OpenDiffDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        var filePath = parameter as string;
        var workspaceSlug = _workspaceSlug;
        var repositorySlug = _repositorySlug;
        var pullRequestId = _pullRequestId;
        var diff = _selectedFileRawDiff;

        if (string.IsNullOrWhiteSpace(filePath) ||
            !string.Equals(filePath, SelectedPath, StringComparison.Ordinal) ||
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
        DiffStatus = $"Abriendo diff nativo de Visual Studio para {filePath}...";
        try
        {
            var comparison = await _pullRequestChangesService.GetFileComparisonAsync(
                workspaceSlug,
                repositorySlug,
                fileChange,
                cancellationToken);
            var beforePath = comparison.Before?.Path ?? fileChange.Before?.Path ?? filePath;
            var afterPath = comparison.After?.Path ?? fileChange.After?.Path ?? filePath;
            await _diffDocumentService.OpenAsync(
                new PullRequestDiffDocument(
                    workspaceSlug,
                    repositorySlug,
                    pullRequestId.Value,
                    beforePath,
                    fileChange.Before is null
                        ? "Base · archivo inexistente"
                        : $"Base · {ShortRevision(fileChange.Before.Revision)}",
                    comparison.Before?.Content ?? string.Empty,
                    afterPath,
                    fileChange.After is null
                        ? "PR · archivo eliminado"
                        : $"PR · {ShortRevision(fileChange.After.Revision)}",
                    comparison.After?.Content ?? string.Empty,
                    diff,
                    Array.Empty<PullRequestDiffComment>()),
                cancellationToken);
            DiffStatus = $"Diff nativo de Visual Studio abierto para {filePath}.";
        }
        catch (OperationCanceledException)
        {
            DiffStatus = "Se canceló la apertura del diff.";
        }
        catch (Exception exception)
        {
            DiffStatus = $"No se pudo abrir el diff de Visual Studio: {exception.Message}";
        }
        finally
        {
            CanOpenDiffDocument =
                string.Equals(filePath, SelectedPath, StringComparison.Ordinal) &&
                string.Equals(diff, _selectedFileRawDiff, StringComparison.Ordinal);
        }
    }

    private bool IsCurrentLoad(long loadVersion) =>
        loadVersion == Volatile.Read(ref _loadVersion);

    private void OpenDiffReply(IPullRequestReplyTarget row)
    {
        if (!row.CanReply || !ContainsDiffComment(row))
        {
            return;
        }

        if (!ReferenceEquals(_activeDiffReplyRow, row))
        {
            CloseActiveDiffReply();
            _activeDiffReplyRow = row;
        }

        row.OpenReplyEditor();
    }

    private async Task LocalizeCommentImagesAsync(
        IReadOnlyList<PullRequestDiffRow> rows,
        string workspaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken)
    {
        var imageBlocks = rows
            .Where(row => row.RowType == "Comment")
            .SelectMany(row => row.ContentBlocks.Concat(
                row.Replies.SelectMany(reply => reply.ContentBlocks)))
            .Where(block => block.Kind == "Image" && !string.IsNullOrWhiteSpace(block.ImageUrl))
            .GroupBy(block => block.ImageUrl, StringComparer.Ordinal)
            .ToArray();

        foreach (var imageGroup in imageBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sourceUri = new Uri(imageGroup.Key, UriKind.Absolute);
                var image = await _pullRequestCommentService.GetImageAsync(
                    workspaceSlug,
                    repositorySlug,
                    sourceUri,
                    cancellationToken);
                var localImageUrl = await PullRequestCommentImageCache.StoreAsync(
                    sourceUri,
                    image,
                    cancellationToken);
                foreach (var block in imageGroup)
                {
                    block.UseLocalImage(localImageUrl);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                foreach (var block in imageGroup)
                {
                    block.SetImageUnavailable();
                }
            }
        }
    }

    private bool ContainsDiffComment(IPullRequestReplyTarget row) =>
        DiffRows.Any(candidate =>
            ReferenceEquals(candidate, row) ||
            candidate.Replies.Any(reply => ReferenceEquals(reply, row)));

    private void CancelDiffReply(IPullRequestReplyTarget row)
    {
        if (ReferenceEquals(_activeDiffReplyRow, row))
        {
            _activeDiffReplyRow = null;
        }

        row.CloseReplyEditor(clearDraft: true);
    }

    private async Task SendDiffReplyAsync(
        IPullRequestReplyTarget row,
        string markdownContent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            row.SetReplyStatus("Escribí una respuesta antes de enviarla.");
            return;
        }

        var workspaceSlug = _workspaceSlug;
        var repositorySlug = _repositorySlug;
        var pullRequestId = _pullRequestId;
        var filePath = SelectedPath;
        var commentId = row.CommentId;
        var loadVersion = Volatile.Read(ref _loadVersion);
        var diffVersion = Volatile.Read(ref _diffVersion);
        if (string.IsNullOrWhiteSpace(workspaceSlug) ||
            string.IsNullOrWhiteSpace(repositorySlug) ||
            pullRequestId is null or <= 0 ||
            string.IsNullOrWhiteSpace(filePath) ||
            commentId is null or <= 0 ||
            !IsCurrentFileSelection(filePath, loadVersion, diffVersion))
        {
            row.SetReplyStatus("El archivo o el Pull Request cambió. Volvé a abrir la respuesta.");
            return;
        }

        row.SetReplyStatus("Enviando respuesta...");
        try
        {
            await _pullRequestCommentService.CreateReplyAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId.Value,
                commentId.Value,
                markdownContent,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                row.SetReplyStatus("Se canceló el envío de la respuesta.");
            }

            return;
        }
        catch (Exception exception)
        {
            if (IsCurrentFileSelection(filePath, loadVersion, diffVersion))
            {
                row.SetReplyStatus($"No se pudo enviar la respuesta: {exception.Message}");
            }

            return;
        }

        if (!IsCurrentFileSelection(filePath, loadVersion, diffVersion))
        {
            return;
        }

        _activeDiffReplyRow = null;
        row.CloseReplyEditor(clearDraft: true);
        LoadSelectedFileDiff(filePath);
    }

    private void CloseActiveDiffReply()
    {
        _activeDiffReplyRow?.CloseReplyEditor(clearDraft: true);
        _activeDiffReplyRow = null;
    }

    private async Task<(IReadOnlyList<BitbucketPullRequestComment> Comments, string? ErrorMessage)>
        GetFileCommentsSafelyAsync(
            string workspaceSlug,
            string repositorySlug,
            long pullRequestId,
            string filePath,
            CancellationToken cancellationToken)
    {
        try
        {
            var comments = await _pullRequestCommentService.GetUnresolvedForFileAsync(
                workspaceSlug,
                repositorySlug,
                pullRequestId,
                filePath,
                cancellationToken);
            return (comments, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (Array.Empty<BitbucketPullRequestComment>(), exception.Message);
        }
    }

    private bool IsCurrentFileSelection(string filePath, long loadVersion, long diffVersion) =>
        IsCurrentLoad(loadVersion) &&
        diffVersion == Volatile.Read(ref _diffVersion) &&
        string.Equals(SelectedPath, filePath, StringComparison.Ordinal);

    private void ClearFiles()
    {
        IsSelectorEnabled = false;
        Tree.Clear();
        _changedFilesByPath.Clear();
        SetSelectedPathWithoutLoading(null);
        Status = "Seleccioná un Pull Request para cargar sus archivos modificados.";
        ResetDiff();
    }

    private void ResetDiff()
    {
        CloseActiveDiffReply();
        _selectedFileRawDiff = null;
        CanOpenDiffDocument = false;
        SelectedFileTitle = "Diff";
        SetDiffPlaceholder("Seleccioná un archivo para visualizar sus cambios.");
        DiffStatus = "Seleccioná un archivo modificado para cargar su diff.";
    }

    private void SetDiffPlaceholder(string message)
    {
        DiffRows.Clear();
        DiffRows.Add(PullRequestDiffRow.CreateMeta(message));
    }

    private static string BuildDiffStatus(int commentCount, string? commentsError)
    {
        if (!string.IsNullOrWhiteSpace(commentsError))
        {
            return $"Diff listo, pero no se pudieron cargar los comentarios: {commentsError}";
        }

        return commentCount switch
        {
            0 => "Diff listo · no hay comentarios sin resolver en este archivo.",
            1 => "Diff listo · 1 comentario sin resolver mostrado en su línea.",
            _ => $"Diff listo · {commentCount} comentarios sin resolver mostrados en sus líneas.",
        };
    }

    private void SetSelectedPathWithoutLoading(string? filePath)
    {
        _suppressSelectionLoad = true;
        try
        {
            SelectedPath = filePath;
        }
        finally
        {
            _suppressSelectionLoad = false;
        }
    }

    private static string ShortRevision(string revision) =>
        revision.Length <= 8 ? revision : revision[..8];
}

internal static class PullRequestFileTreeBuilder
{
    internal static IReadOnlyList<PullRequestFileTreeNode> Build(
        IReadOnlyList<BitbucketPullRequestFileChange> files,
        Action<string> onFileSelected)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(onFileSelected);

        var roots = new ObservableCollection<PullRequestFileTreeNode>();
        foreach (var file in files)
        {
            var segments = file.Path
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentNodes = roots;
            var parentFolders = new List<PullRequestFileTreeNode>();
            for (var index = 0; index < segments.Length - 1; index++)
            {
                var folderName = segments[index];
                var folder = currentNodes.FirstOrDefault(node =>
                    !node.IsFile && string.Equals(node.Name, folderName, StringComparison.Ordinal));
                if (folder is null)
                {
                    folder = PullRequestFileTreeNode.CreateFolder(folderName, index);
                    InsertTreeNode(currentNodes, folder);
                }

                parentFolders.Add(folder);
                currentNodes = folder.Children;
            }

            InsertTreeNode(
                currentNodes,
                PullRequestFileTreeNode.CreateFile(
                    new PullRequestFileListItem(file),
                    onFileSelected));
            foreach (var folder in parentFolders)
            {
                folder.IncrementFileCount();
            }
        }

        return roots.ToArray();
    }

    private static void InsertTreeNode(
        ObservableCollection<PullRequestFileTreeNode> nodes,
        PullRequestFileTreeNode node)
    {
        var index = 0;
        while (index < nodes.Count && CompareTreeNodes(nodes[index], node) <= 0)
        {
            index++;
        }

        nodes.Insert(index, node);
    }

    private static int CompareTreeNodes(PullRequestFileTreeNode left, PullRequestFileTreeNode right)
    {
        var typeComparison = left.IsFile.CompareTo(right.IsFile);
        return typeComparison != 0
            ? typeComparison
            : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }
}

[DataContract]
internal sealed class PullRequestFileListItem
{
    public PullRequestFileListItem(BitbucketPullRequestFileChange file)
    {
        Path = file.Path;
        FileName = System.IO.Path.GetFileName(file.Path);
        Change = TranslateStatus(file.Status);
        ChangeMarker = BuildChangeMarker(file.Status);
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
    public string Change { get; }

    [DataMember]
    public string ChangeMarker { get; }

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

    private static string BuildChangeMarker(string status) => status.ToLowerInvariant() switch
    {
        "added" => "+",
        "removed" => "−",
        "renamed" => "→",
        _ => "●",
    };
}

[DataContract]
internal sealed class PullRequestFileTreeNode : NotifyPropertyChangedObject
{
    private readonly Action<string>? _onFileSelected;
    private bool _isExpanded;
    private bool _isSelected;
    private int _fileCount;
    private string _details;

    private PullRequestFileTreeNode(
        string name,
        string glyph,
        string details,
        string lineSummary,
        string? path,
        bool isExpanded,
        Action<string>? onFileSelected)
    {
        Name = name;
        Glyph = glyph;
        _details = details;
        LineSummary = lineSummary;
        Path = path;
        IsFile = path is not null;
        _isExpanded = isExpanded;
        _onFileSelected = onFileSelected;
    }

    [DataMember]
    public string Name { get; }

    [DataMember]
    public string Glyph { get; }

    [DataMember]
    public string Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    [DataMember]
    public string LineSummary { get; }

    [DataMember]
    public string? Path { get; }

    [DataMember]
    public ObservableCollection<PullRequestFileTreeNode> Children { get; } = [];

    [DataMember]
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    [DataMember]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value) || !value || Path is null)
            {
                return;
            }

            _onFileSelected?.Invoke(Path);
        }
    }

    public bool IsFile { get; }

    public static PullRequestFileTreeNode CreateFolder(string name, int depth) =>
        new(name, "📁", "0 archivos", string.Empty, null, depth < 3, null);

    public static PullRequestFileTreeNode CreateFile(
        PullRequestFileListItem file,
        Action<string> onFileSelected) =>
        new(
            file.FileName,
            file.ChangeMarker,
            string.IsNullOrWhiteSpace(file.PreviousPath)
                ? file.Change
                : $"{file.Change} · {file.PreviousPath}",
            file.LineSummary,
            file.Path,
            false,
            onFileSelected);

    public void IncrementFileCount()
    {
        _fileCount++;
        Details = _fileCount == 1 ? "1 archivo" : $"{_fileCount} archivos";
    }
}
