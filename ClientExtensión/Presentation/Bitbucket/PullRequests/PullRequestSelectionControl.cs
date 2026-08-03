using ClientExtension.Presentation.Shared.Layout;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

internal sealed class PullRequestSelectionControl : ThemedRemoteUserControl
{
    private readonly PullRequestSelectionData _dataContext;

    public PullRequestSelectionControl(PullRequestSelectionData dataContext)
        : base(dataContext)
    {
        _dataContext = dataContext;
    }

    public override async Task ControlLoadedAsync(CancellationToken cancellationToken)
    {
        await base.ControlLoadedAsync(cancellationToken);
        await _dataContext.EnsureWorkspacesLoadedAsync(cancellationToken);
    }
}
