using ClientExtension.Presentation.Bitbucket.PullRequests.Data;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Commands;

[VisualStudioContribution]
internal sealed class ShowPullRequestSelectionCommand : Command
{
    private readonly PullRequestSelectionData _dataContext;

    public ShowPullRequestSelectionCommand(
        VisualStudioExtensibility extensibility,
        PullRequestSelectionData dataContext)
        : base(extensibility)
    {
        _dataContext = dataContext;
    }

    public override CommandConfiguration CommandConfiguration =>
        new("%ClientExtension.ShowPullRequestSelectionCommand.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.ToolWindow, IconSettings.IconAndText),
            Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
        };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await Extensibility.Shell().ShowToolWindowAsync<PullRequestSelectionToolWindow>(
            activate: true,
            cancellationToken);
        await _dataContext.EnsureWorkspacesLoadedAsync(cancellationToken);
    }
}
