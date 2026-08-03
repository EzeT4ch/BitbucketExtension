using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ClientExtension.Presentation.IntegrationHub;

[VisualStudioContribution]
internal sealed class IntegrationHubToolWindow : ToolWindow
{
    private readonly IntegrationHubData _dataContext;

    public IntegrationHubToolWindow(
        VisualStudioExtensibility extensibility,
        IntegrationHubData dataContext)
        : base(extensibility)
    {
        _dataContext = dataContext;
        Title = "Centro de integraciones";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
        DockDirection = Dock.Right,
    };

    public override Task InitializeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        => Task.FromResult<IRemoteUserControl>(new IntegrationHubControl(_dataContext));
}
