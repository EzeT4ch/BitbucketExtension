using BitbucketAdapter.Domain.Auth.AppPassword;

namespace BitbucketAdapter.Application.Abstractions.AppPassword;

public interface IAppPasswordPrompt
{
    Task<BitbucketAppPasswordCredential> RequestCredentialAsync(CancellationToken cancellationToken);
}
