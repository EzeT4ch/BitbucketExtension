using System.Runtime.Serialization;
using BitbucketAdapter.Domain.PullRequests;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Data;

internal interface IPullRequestCommentActionHandler
{
    Task SaveEditAsync(PullRequestCommentActions actions, string markdownContent, CancellationToken cancellationToken);

    Task DeleteAsync(PullRequestCommentActions actions, CancellationToken cancellationToken);

    Task SetResolvedAsync(PullRequestCommentActions actions, bool isResolved, CancellationToken cancellationToken);
}

[DataContract]
internal sealed class PullRequestCommentActions : NotifyPropertyChangedObject
{
    private readonly string _originalContent;
    private string _editContent = string.Empty;
    private string _status = string.Empty;
    private bool _isEditorOpen;
    private bool _isDeleteConfirmationVisible;
    private bool _isBusy;

    private PullRequestCommentActions(
        long commentId,
        string originalContent,
        bool canEdit,
        bool canDelete,
        bool canResolve,
        bool canReopen,
        IPullRequestCommentActionHandler handler)
    {
        CommentId = commentId;
        _originalContent = originalContent;
        CanEdit = canEdit;
        CanDelete = canDelete;
        CanResolve = canResolve;
        CanReopen = canReopen;

        EditCommand = new AsyncCommand((_, _) =>
        {
            OpenEditor();
            return Task.CompletedTask;
        })
        {
            CanExecute = canEdit,
        };
        CancelEditCommand = new AsyncCommand((_, _) =>
        {
            CloseEditor();
            return Task.CompletedTask;
        });
        SaveEditCommand = new AsyncCommand((parameter, cancellationToken) =>
            handler.SaveEditAsync(this, parameter as string ?? EditContent, cancellationToken))
        {
            CanExecute = false,
        };
        DeleteCommand = new AsyncCommand((_, _) =>
        {
            Status = string.Empty;
            IsDeleteConfirmationVisible = true;
            return Task.CompletedTask;
        })
        {
            CanExecute = canDelete,
        };
        CancelDeleteCommand = new AsyncCommand((_, _) =>
        {
            IsDeleteConfirmationVisible = false;
            return Task.CompletedTask;
        });
        ConfirmDeleteCommand = new AsyncCommand((_, cancellationToken) =>
            handler.DeleteAsync(this, cancellationToken))
        {
            CanExecute = canDelete,
        };
        ResolveCommand = new AsyncCommand((_, cancellationToken) =>
            handler.SetResolvedAsync(this, true, cancellationToken))
        {
            CanExecute = canResolve,
        };
        ReopenCommand = new AsyncCommand((_, cancellationToken) =>
            handler.SetResolvedAsync(this, false, cancellationToken))
        {
            CanExecute = canReopen,
        };
    }

    public long CommentId { get; }

    [DataMember]
    public bool CanEdit { get; }

    [DataMember]
    public bool CanDelete { get; }

    [DataMember]
    public bool CanResolve { get; }

    [DataMember]
    public bool CanReopen { get; }

    [DataMember]
    public bool HasAnyAction => CanEdit || CanDelete || CanResolve || CanReopen;

    [DataMember]
    public string EditContent
    {
        get => _editContent;
        set
        {
            if (SetProperty(ref _editContent, value))
            {
                SaveEditCommand.CanExecute = CanEdit && !IsBusy && !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    [DataMember]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [DataMember]
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => SetProperty(ref _isEditorOpen, value);
    }

    [DataMember]
    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set => SetProperty(ref _isDeleteConfirmationVisible, value);
    }

    [DataMember]
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            EditCommand.CanExecute = CanEdit && !value;
            SaveEditCommand.CanExecute = CanEdit && !value && !string.IsNullOrWhiteSpace(EditContent);
            DeleteCommand.CanExecute = CanDelete && !value;
            ConfirmDeleteCommand.CanExecute = CanDelete && !value;
            ResolveCommand.CanExecute = CanResolve && !value;
            ReopenCommand.CanExecute = CanReopen && !value;
        }
    }

    [DataMember]
    public AsyncCommand EditCommand { get; }

    [DataMember]
    public AsyncCommand SaveEditCommand { get; }

    [DataMember]
    public AsyncCommand CancelEditCommand { get; }

    [DataMember]
    public AsyncCommand DeleteCommand { get; }

    [DataMember]
    public AsyncCommand ConfirmDeleteCommand { get; }

    [DataMember]
    public AsyncCommand CancelDeleteCommand { get; }

    [DataMember]
    public AsyncCommand ResolveCommand { get; }

    [DataMember]
    public AsyncCommand ReopenCommand { get; }

    internal static PullRequestCommentActions Create(
        BitbucketPullRequestComment comment,
        string? currentUserUuid,
        IPullRequestCommentActionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(handler);

        var isOwnComment = !comment.IsDeleted && IsSameUser(comment.AuthorUuid, currentUserUuid);
        var isRootComment = comment.ParentId is null && !comment.IsDeleted;
        return new PullRequestCommentActions(
            comment.Id,
            comment.Content,
            canEdit: isOwnComment,
            canDelete: isOwnComment,
            canResolve: isRootComment && !comment.IsResolved,
            canReopen: isRootComment && comment.IsResolved,
            handler);
    }

    internal static bool IsSameUser(string? left, string? right)
    {
        var normalizedLeft = NormalizeUuid(left);
        var normalizedRight = NormalizeUuid(right);
        return normalizedLeft.Length > 0 &&
               string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    internal void CloseEditor()
    {
        IsEditorOpen = false;
        EditContent = string.Empty;
        Status = string.Empty;
    }

    internal void SetStatus(string status)
    {
        Status = status;
    }

    internal async Task<bool> RunAsync(
        string busyStatus,
        string errorPrefix,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        Status = busyStatus;
        try
        {
            await operation(cancellationToken);
            Status = string.Empty;
            IsDeleteConfirmationVisible = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "Se canceló la operación.";
            return false;
        }
        catch (Exception exception)
        {
            Status = $"{errorPrefix}: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenEditor()
    {
        IsDeleteConfirmationVisible = false;
        EditContent = _originalContent;
        Status = string.Empty;
        IsEditorOpen = true;
    }

    private static string NormalizeUuid(string? value) =>
        value?.Trim().Trim('{', '}').Trim() ?? string.Empty;
}
