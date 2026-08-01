using BitbucketAdapter.Domain.Auth.AppPassword;

namespace BitbucketAdapter.Application.Abstractions.AppPassword;

public interface IAppPasswordCredentialStore
{
    Task<BitbucketAppPasswordCredential?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(BitbucketAppPasswordCredential credential, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
