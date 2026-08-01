using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Auth;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Commands;

[VisualStudioContribution]
internal class UseBitbucketAppPasswordCommand : Command
{
    private readonly IBitbucketAuthMethodStore _authMethodStore;

    public UseBitbucketAppPasswordCommand(VisualStudioExtensibility extensibility, IBitbucketAuthMethodStore authMethodStore)
        : base(extensibility)
    {
        _authMethodStore = authMethodStore;
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.UseBitbucketAppPasswordCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await _authMethodStore.SaveAsync(BitbucketAuthMethod.AppPassword, cancellationToken);
        await Extensibility.Shell().ShowPromptAsync(
            "Bitbucket: se usará App Password. Se pedirá usuario y App Password la próxima vez que haga falta conectar.",
            PromptOptions.OK,
            cancellationToken);
    }
}
