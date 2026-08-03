using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.IntegrationHub;

internal sealed class IntegrationHubControl : RemoteUserControl
{
    private readonly IntegrationHubData _dataContext;

    public IntegrationHubControl(IntegrationHubData dataContext)
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
