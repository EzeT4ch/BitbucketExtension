using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Domain.Exceptions;
using ClientExtension.Presentation.Bitbucket.Authentication;
using ClientExtension.Presentation.Bitbucket.PullRequests;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Presentation.Bitbucket.Authentication.Commands;

[VisualStudioContribution]
internal class UseBitbucketOAuthCommand : Command
{
    private readonly IBitbucketOAuthConnectionService _connectionService;
    private readonly BitbucketAuthenticationCommandVisibility _commandVisibility;
    private readonly PullRequestSelectionData _pullRequestSelectionData;

    public UseBitbucketOAuthCommand(
        VisualStudioExtensibility extensibility,
        IBitbucketOAuthConnectionService connectionService,
        BitbucketAuthenticationCommandVisibility commandVisibility,
        PullRequestSelectionData pullRequestSelectionData)
        : base(extensibility)
    {
        _connectionService = connectionService;
        _commandVisibility = commandVisibility;
        _pullRequestSelectionData = pullRequestSelectionData;
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.UseBitbucketOAuthCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Action, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await base.InitializeAsync(cancellationToken);
        await _commandVisibility.RegisterAsync(
            this,
            isVisibleWhenConnected: false,
            cancellationToken);
    }

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        try
        {
            var shouldOpenBrowser = await Extensibility.Shell().ShowPromptAsync(
                "Se abrirá el navegador predeterminado para conectar Bitbucket con OAuth 2.0. Continuá allí y luego regresá a Visual Studio.",
                PromptOptions.OKCancel,
                cancellationToken);
            if (!shouldOpenBrowser)
            {
                return;
            }

            await _connectionService.ConnectAsync(cancellationToken);
            await _commandVisibility.RefreshAsync(cancellationToken);
            _pullRequestSelectionData.InvalidateWorkspaces();
            await Extensibility.Shell().ShowToolWindowAsync<PullRequestSelectionToolWindow>(
                activate: true,
                cancellationToken);
            await _pullRequestSelectionData.EnsureWorkspacesLoadedAsync(cancellationToken);
            await Extensibility.Shell().ShowPromptAsync(
                "Bitbucket: OAuth 2.0 conectado correctamente.",
                PromptOptions.OK,
                cancellationToken);
        }
        catch (BitbucketAuthenticationException exception)
        {
            await Extensibility.Shell().ShowPromptAsync(exception.Message, PromptOptions.OK, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Extensibility.Shell().ShowPromptAsync(
                $"Error inesperado al conectar con Bitbucket: {exception.Message}",
                PromptOptions.OK,
                cancellationToken);
        }
    }
}
