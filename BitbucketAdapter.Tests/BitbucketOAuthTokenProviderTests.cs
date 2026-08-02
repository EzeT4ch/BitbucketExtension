using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Application.Auth.OAuth;
using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Domain.Exceptions;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketOAuthTokenProviderTests
{
    [Fact]
    public async Task GetValidTokenAsync_WhenCachedTokenIsValid_DoesNotAuthorizeAgain()
    {
        var token = CreateToken();
        var store = new FakeTokenStore(token);
        var exchangeClient = new FakeExchangeClient();
        var authorizationCodeProvider = new FakeAuthorizationCodeProvider();
        var provider = new BitbucketOAuthTokenProvider(store, exchangeClient, authorizationCodeProvider);

        var result = await provider.GetValidTokenAsync(CancellationToken.None);

        Assert.Equal(token, result);
        Assert.Equal(0, authorizationCodeProvider.CallCount);
        Assert.Equal(0, exchangeClient.ExchangeAuthorizationCodeCallCount);
    }

    [Fact]
    public async Task GetValidTokenAsync_WhenNoTokenExists_AuthorizesExchangesAndPersistsToken()
    {
        var expectedToken = CreateToken();
        var store = new FakeTokenStore();   
        var exchangeClient = new FakeExchangeClient(expectedToken);
        var authorizationCodeProvider = new FakeAuthorizationCodeProvider("code");
        var provider = new BitbucketOAuthTokenProvider(store, exchangeClient, authorizationCodeProvider);

        var result = await provider.GetValidTokenAsync(CancellationToken.None);

        Assert.Equal(expectedToken, result);
        Assert.Equal(1, authorizationCodeProvider.CallCount);
        Assert.Equal(1, exchangeClient.ExchangeAuthorizationCodeCallCount);
        Assert.Equal(expectedToken, store.SavedToken);
    }

    [Fact]
    public async Task GetValidTokenAsync_WhenRefreshFails_AuthorizesAgain()
    {
        var refreshedByAuthorization = CreateToken();
        var expiredToken = new OAuthToken("expired", "refresh", "Bearer", DateTimeOffset.UtcNow.AddMinutes(-1));
        var store = new FakeTokenStore(expiredToken);
        var exchangeClient = new FakeExchangeClient(refreshedByAuthorization, new BitbucketAuthenticationException("Refresh revocado."));
        var authorizationCodeProvider = new FakeAuthorizationCodeProvider("new-code");
        var provider = new BitbucketOAuthTokenProvider(store, exchangeClient, authorizationCodeProvider);

        var result = await provider.GetValidTokenAsync(CancellationToken.None);

        Assert.Equal(refreshedByAuthorization, result);
        Assert.Equal(1, exchangeClient.RefreshCallCount);
        Assert.Equal(1, authorizationCodeProvider.CallCount);
        Assert.Equal(1, exchangeClient.ExchangeAuthorizationCodeCallCount);
    }

    private static OAuthToken CreateToken() => new("access", "refresh", "Bearer", DateTimeOffset.UtcNow.AddHours(1));

    private sealed class FakeTokenStore : IOAuthTokenStore
    {
        private OAuthToken? _token;

        public FakeTokenStore(OAuthToken? token = null)
        {
            _token = token;
        }

        public OAuthToken? SavedToken { get; private set; }

        public Task<OAuthToken?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_token);

        public Task SaveAsync(OAuthToken token, CancellationToken cancellationToken)
        {
            _token = token;
            SavedToken = token;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            _token = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExchangeClient : IOAuthTokenExchangeClient
    {
        private readonly OAuthToken _authorizationCodeToken;
        private readonly Exception? _refreshException;

        public FakeExchangeClient(OAuthToken? authorizationCodeToken = null, Exception? refreshException = null)
        {
            _authorizationCodeToken = authorizationCodeToken ?? CreateToken();
            _refreshException = refreshException;
        }

        public int ExchangeAuthorizationCodeCallCount { get; private set; }

        public int RefreshCallCount { get; private set; }

        public Task<OAuthToken> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
        {
            ExchangeAuthorizationCodeCallCount++;
            return Task.FromResult(_authorizationCodeToken);
        }

        public Task<OAuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshCallCount++;
            return _refreshException is null
                ? Task.FromResult(_authorizationCodeToken)
                : Task.FromException<OAuthToken>(_refreshException);
        }
    }

    private sealed class FakeAuthorizationCodeProvider : IAuthorizationCodeProvider
    {
        private readonly string _authorizationCode;

        public FakeAuthorizationCodeProvider(string authorizationCode = "code")
        {
            _authorizationCode = authorizationCode;
        }

        public int CallCount { get; private set; }

        public Task<string> GetAuthorizationCodeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_authorizationCode);
        }
    }
}
