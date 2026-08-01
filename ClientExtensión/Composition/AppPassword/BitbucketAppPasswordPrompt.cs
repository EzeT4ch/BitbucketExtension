using BitbucketAdapter.Application.Abstractions.AppPassword;
using BitbucketAdapter.Domain.Auth.AppPassword;
using BitbucketAdapter.Domain.Exceptions;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Composition.AppPassword;

internal sealed class BitbucketAppPasswordPrompt : IAppPasswordPrompt
{
    private readonly VisualStudioExtensibility _extensibility;

    public BitbucketAppPasswordPrompt(VisualStudioExtensibility extensibility)
    {
        _extensibility = extensibility;
    }

    public async Task<BitbucketAppPasswordCredential> RequestCredentialAsync(CancellationToken cancellationToken)
    {
        var username = await _extensibility.Shell().ShowPromptAsync(
            "Usuario o email de Bitbucket:",
            InputPromptOptions.Default,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new BitbucketAuthenticationException("Se canceló el ingreso de credenciales de Bitbucket.");
        }

        var appPassword = await _extensibility.Shell().ShowPromptAsync(
            "App Password de Bitbucket (Personal settings > App passwords):",
            InputPromptOptions.Default,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(appPassword))
        {
            throw new BitbucketAuthenticationException("Se canceló el ingreso de credenciales de Bitbucket.");
        }

        return new BitbucketAppPasswordCredential(username.Trim(), appPassword);
    }
}
