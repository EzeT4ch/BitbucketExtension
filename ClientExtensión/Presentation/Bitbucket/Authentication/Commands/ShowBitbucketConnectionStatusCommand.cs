using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using ClientExtension.Presentation.Bitbucket.Authentication;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Presentation.Bitbucket.Authentication.Commands;

[VisualStudioContribution]
internal class ShowBitbucketConnectionStatusCommand : Command
{
    private readonly IBitbucketConnectionStatusService _connectionStatusService;
    private readonly BitbucketAuthenticationCommandVisibility _commandVisibility;

    public ShowBitbucketConnectionStatusCommand(
        VisualStudioExtensibility extensibility,
        IBitbucketConnectionStatusService connectionStatusService,
        BitbucketAuthenticationCommandVisibility commandVisibility)
        : base(extensibility)
    {
        _connectionStatusService = connectionStatusService;
        _commandVisibility = commandVisibility;
        SetVisibilityState(false);
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.ShowBitbucketConnectionStatusCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Action, IconSettings.IconAndText),
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
        try
        {
            var connectionStatus = await _connectionStatusService.GetAsync(cancellationToken);
            await Extensibility.Shell().ShowPromptAsync(
                $"Conectado. Status: {connectionStatus.StatusCode}", PromptOptions.OK, cancellationToken);
        }
        catch (BitbucketAuthenticationException exception)
        {
            await Extensibility.Shell().ShowPromptAsync(exception.Message, PromptOptions.OK, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Extensibility.Shell().ShowPromptAsync(
                $"Error inesperado al conectar con Bitbucket: {exception.Message}", PromptOptions.OK, cancellationToken);
        }
    }
}
