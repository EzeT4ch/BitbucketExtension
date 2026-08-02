using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Domain.Auth;

namespace BitbucketAdapter.Application.Auth.OAuth;

public sealed class BitbucketOAuthConnectionService : IBitbucketOAuthConnectionService
{
    private readonly IBitbucketAuthMethodStore _authMethodStore;
    private readonly Func<IBitbucketOAuthTokenProvider> _tokenProviderFactory;
    private readonly IOAuthTokenStore _tokenStore;

    public BitbucketOAuthConnectionService(
        IBitbucketAuthMethodStore authMethodStore,
        Func<IBitbucketOAuthTokenProvider> tokenProviderFactory,
        IOAuthTokenStore tokenStore)
    {
        _authMethodStore = authMethodStore;
        _tokenProviderFactory = tokenProviderFactory;
        _tokenStore = tokenStore;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _tokenProviderFactory().GetValidTokenAsync(cancellationToken);
        await _authMethodStore.SaveAsync(BitbucketAuthMethod.OAuth2, cancellationToken);
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken)
    {
        var method = await _authMethodStore.GetAsync(cancellationToken);
        if (method != BitbucketAuthMethod.OAuth2)
        {
            return false;
        }

        return await _tokenStore.GetAsync(cancellationToken) is not null;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _tokenStore.DeleteAsync(cancellationToken);
        await _authMethodStore.ClearAsync(cancellationToken);
    }
}
