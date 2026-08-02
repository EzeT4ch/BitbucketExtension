using BitbucketAdapter.Domain.Auth.OAuth;

namespace BitbucketAdapter.Application.Abstractions.OAuth;

public interface IOAuthTokenStore
{
    Task<OAuthToken?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(OAuthToken token, CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}
