using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Auth;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Commands;

[VisualStudioContribution]
internal class UseBitbucketOAuthCommand : Command
{
    private readonly IBitbucketAuthMethodStore _authMethodStore;

    public UseBitbucketOAuthCommand(VisualStudioExtensibility extensibility, IBitbucketAuthMethodStore authMethodStore)
        : base(extensibility)
    {
        _authMethodStore = authMethodStore;
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.UseBitbucketOAuthCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await _authMethodStore.SaveAsync(BitbucketAuthMethod.OAuth2, cancellationToken);
        await Extensibility.Shell().ShowPromptAsync(
            "Bitbucket: se usará OAuth 2.0. Se abrirá el navegador para iniciar sesión la próxima vez que haga falta conectar.",
            PromptOptions.OK,
            cancellationToken);
    }
}
