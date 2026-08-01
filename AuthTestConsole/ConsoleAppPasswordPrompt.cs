using BitbucketAdapter.Application.Abstractions.AppPassword;
using BitbucketAdapter.Domain.Auth.AppPassword;

namespace AuthTestConsole;

internal sealed class ConsoleAppPasswordPrompt : IAppPasswordPrompt
{
    public Task<BitbucketAppPasswordCredential> RequestCredentialAsync(CancellationToken cancellationToken)
    {
        Console.Write("Usuario o email de Bitbucket: ");
        var username = Console.ReadLine() ?? string.Empty;

        Console.Write("App Password de Bitbucket: ");
        var appPassword = Console.ReadLine() ?? string.Empty;

        return Task.FromResult(new BitbucketAppPasswordCredential(username.Trim(), appPassword));
    }
}
