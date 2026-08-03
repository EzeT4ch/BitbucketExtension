using ClientExtension.Presentation.IntegrationHub;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Presentation.IntegrationHub.Commands;

[VisualStudioContribution]
internal sealed class ShowIntegrationHubCommand : Command
{
    private readonly IntegrationHubData _dataContext;

    public ShowIntegrationHubCommand(
        VisualStudioExtensibility extensibility,
        IntegrationHubData dataContext)
        : base(extensibility)
    {
        _dataContext = dataContext;
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.ShowIntegrationHubCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.ToolWindow, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await Extensibility.Shell().ShowToolWindowAsync<IntegrationHubToolWindow>(activate: true, cancellationToken);
        await _dataContext.EnsureWorkspacesLoadedAsync(cancellationToken);
    }
}
