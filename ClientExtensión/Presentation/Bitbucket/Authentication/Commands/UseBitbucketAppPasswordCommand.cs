using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Auth;
using ClientExtension.Presentation.Bitbucket.Authentication;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Presentation.Bitbucket.Authentication.Commands;

[VisualStudioContribution]
internal class UseBitbucketAppPasswordCommand : Command
{
    private readonly IBitbucketAuthMethodStore _authMethodStore;
    private readonly BitbucketAuthenticationCommandVisibility _commandVisibility;

    public UseBitbucketAppPasswordCommand(
        VisualStudioExtensibility extensibility,
        IBitbucketAuthMethodStore authMethodStore,
        BitbucketAuthenticationCommandVisibility commandVisibility)
        : base(extensibility)
    {
        _authMethodStore = authMethodStore;
        _commandVisibility = commandVisibility;
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.UseBitbucketAppPasswordCommand.DisplayName%")
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
        await _authMethodStore.SaveAsync(BitbucketAuthMethod.AppPassword, cancellationToken);
        await _commandVisibility.RefreshAsync(cancellationToken);
        await Extensibility.Shell().ShowPromptAsync(
            "Bitbucket: se usará App Password. Se pedirá usuario y App Password la próxima vez que haga falta conectar.",
            PromptOptions.OK,
            cancellationToken);
    }
}
