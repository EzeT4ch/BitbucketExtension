using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Diff;

[VisualStudioContribution]
internal sealed class PullRequestDiffToolWindow : ToolWindow
{
    private readonly PullRequestDiffViewData _dataContext;

    public PullRequestDiffToolWindow(
        VisualStudioExtensibility extensibility,
        PullRequestDiffViewData dataContext)
        : base(extensibility)
    {
        _dataContext = dataContext;
        Title = "Diff enriquecido de Pull Request";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
        AllowAutoCreation = false,
    };

    public override Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IRemoteUserControl>(new PullRequestDiffControl(_dataContext));
}
