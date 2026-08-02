using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Domain.Exceptions;

namespace BitbucketAdapter.Application.Auth.OAuth;

public sealed class BitbucketOAuthTokenProvider : IBitbucketOAuthTokenProvider
{
    private readonly IOAuthTokenStore _tokenStore;
    private readonly IOAuthTokenExchangeClient _tokenExchangeClient;
    private readonly IAuthorizationCodeProvider _authorizationCodeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public BitbucketOAuthTokenProvider(
        IOAuthTokenStore tokenStore,
        IOAuthTokenExchangeClient tokenExchangeClient,
        IAuthorizationCodeProvider authorizationCodeProvider)
    {
        _tokenStore = tokenStore;
        _tokenExchangeClient = tokenExchangeClient;
        _authorizationCodeProvider = authorizationCodeProvider;
    }

    public async Task<OAuthToken> GetValidTokenAsync(CancellationToken cancellationToken)
    {
        var cachedToken = await _tokenStore.GetAsync(cancellationToken);
        if (cachedToken is not null && !cachedToken.IsExpired(DateTimeOffset.UtcNow))
        {
            return cachedToken;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            cachedToken = await _tokenStore.GetAsync(cancellationToken);
            if (cachedToken is not null && !cachedToken.IsExpired(DateTimeOffset.UtcNow))
            {
                return cachedToken;
            }

            if (cachedToken?.RefreshToken is { } refreshToken)
            {
                try
                {
                    var refreshedToken = await _tokenExchangeClient.RefreshAsync(refreshToken, cancellationToken);
                    await _tokenStore.SaveAsync(refreshedToken, cancellationToken);
                    return refreshedToken;
                }
                catch (BitbucketAuthenticationException)
                {
                    // El refresh token puede haber sido revocado; se cae al flujo completo de autorización.
                }
            }

            var authorizationCode = await _authorizationCodeProvider.GetAuthorizationCodeAsync(cancellationToken);
            var newToken =
                await _tokenExchangeClient.ExchangeAuthorizationCodeAsync(authorizationCode, cancellationToken);
            await _tokenStore.SaveAsync(newToken, cancellationToken);
            return newToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task InvalidateCurrentTokenAsync(CancellationToken cancellationToken)
    {
        var cachedToken = await _tokenStore.GetAsync(cancellationToken);
        if (cachedToken is null)
        {
            return;
        }

        var expiredToken = cachedToken with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
        await _tokenStore.SaveAsync(expiredToken, cancellationToken);
    }
}
