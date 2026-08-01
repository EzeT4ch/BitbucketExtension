using BitbucketAdapter.Domain.Auth.OAuth;

namespace BitbucketAdapter.Application.Abstractions.OAuth;

public interface IOAuthTokenExchangeClient
{
    Task<OAuthToken> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken);

    Task<OAuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
