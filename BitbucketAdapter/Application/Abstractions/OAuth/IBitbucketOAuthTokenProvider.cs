using BitbucketAdapter.Domain.Auth.OAuth;

namespace BitbucketAdapter.Application.Abstractions.OAuth;

public interface IBitbucketOAuthTokenProvider
{
    Task<OAuthToken> GetValidTokenAsync(CancellationToken cancellationToken);

    Task InvalidateCurrentTokenAsync(CancellationToken cancellationToken);
}
