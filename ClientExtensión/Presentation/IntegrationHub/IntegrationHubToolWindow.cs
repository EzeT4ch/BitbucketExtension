using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ClientExtension.Presentation.IntegrationHub;

[VisualStudioContribution]
internal sealed class IntegrationHubToolWindow : ToolWindow
{
    private IntegrationHubData? _dataContext;

    public IntegrationHubToolWindow(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        Title = "Centro de integraciones";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
        DockDirection = Dock.Right,
    };

    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        _dataContext = new IntegrationHubData();
        return Task.CompletedTask;
    }

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        var dataContext = _dataContext ?? throw new InvalidOperationException("La pantalla de integraciones no se inicializó.");
        return Task.FromResult<IRemoteUserControl>(new IntegrationHubControl(dataContext));
    }
}
