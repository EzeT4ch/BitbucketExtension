using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.IntegrationHub;

internal sealed class IntegrationHubControl : RemoteUserControl
{
    public IntegrationHubControl(IntegrationHubData dataContext)
        : base(dataContext)
    {
    }
}
