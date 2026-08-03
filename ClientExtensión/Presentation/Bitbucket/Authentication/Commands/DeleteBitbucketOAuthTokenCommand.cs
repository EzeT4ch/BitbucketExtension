using System.Diagnostics;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Abstractions.OAuth;
using ClientExtension.Presentation.Bitbucket.Authentication;
using ClientExtension.Presentation.IntegrationHub;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Presentation.Bitbucket.Authentication.Commands;

[VisualStudioContribution]
internal sealed class DeleteBitbucketOAuthTokenCommand : Command
{
    private const string AppAuthorizationsUrl = "https://bitbucket.org/account/settings/app-authorizations/";

    private readonly IBitbucketOAuthConnectionService _connectionService;
    private readonly BitbucketAuthenticationCommandVisibility _commandVisibility;
    private readonly IBitbucketWorkspaceSelectionStore _workspaceSelectionStore;
    private readonly IntegrationHubData _integrationHubData;

    public DeleteBitbucketOAuthTokenCommand(
        VisualStudioExtensibility extensibility,
        IBitbucketOAuthConnectionService connectionService,
        BitbucketAuthenticationCommandVisibility commandVisibility,
        IBitbucketWorkspaceSelectionStore workspaceSelectionStore,
        IntegrationHubData integrationHubData)
        : base(extensibility)
    {
        _connectionService = connectionService;
        _commandVisibility = commandVisibility;
        _workspaceSelectionStore = workspaceSelectionStore;
        _integrationHubData = integrationHubData;
        SetVisibilityState(false);
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.DeleteBitbucketOAuthTokenCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await base.InitializeAsync(cancellationToken);
        await _commandVisibility.RegisterAsync(
            this,
            isVisibleWhenConnected: true,
            cancellationToken);
    }

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var shouldDisconnect = await Extensibility.Shell().ShowPromptAsync(
            "Se eliminará el token OAuth almacenado en este equipo. Después podrá revocar el acceso remoto desde Bitbucket.",
            PromptOptions.OKCancel,
            cancellationToken);
        if (!shouldDisconnect)
        {
            return;
        }

        try
        {
            await _connectionService.DisconnectAsync(cancellationToken);
            await _workspaceSelectionStore.ClearAsync(cancellationToken);
            _integrationHubData.ClearWorkspaces();
            await _commandVisibility.RefreshAsync(cancellationToken);
            OpenAppAuthorizationsPage();
            await Extensibility.Shell().ShowPromptAsync(
                "El token local se eliminó. Se abrió Bitbucket para revocar el acceso remoto de la aplicación.",
                PromptOptions.OK,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Extensibility.Shell().ShowPromptAsync(
                $"No se pudo eliminar el token de Bitbucket: {exception.Message}",
                PromptOptions.OK,
                cancellationToken);
        }
    }

    private static void OpenAppAuthorizationsPage()
    {
        Process.Start(new ProcessStartInfo(AppAuthorizationsUrl) { UseShellExecute = true });
    }
}
