using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

[VisualStudioContribution]
internal sealed class PullRequestSelectionToolWindow : ToolWindow
{
    private readonly PullRequestSelectionData _dataContext;

    public PullRequestSelectionToolWindow(
        VisualStudioExtensibility extensibility,
        PullRequestSelectionData dataContext)
        : base(extensibility)
    {
        _dataContext = dataContext;
        Title = "Pull Requests de Bitbucket";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
        DockDirection = Dock.Right,
    };

    public override Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IRemoteUserControl>(new PullRequestSelectionControl(_dataContext));
}
