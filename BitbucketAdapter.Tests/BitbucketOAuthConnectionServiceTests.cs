using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Application.Auth.OAuth;
using BitbucketAdapter.Domain.Auth;
using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Domain.Exceptions;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketOAuthConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_WhenTokenIsAvailable_SavesOAuthAsSelectedMethod()
    {
        var methodStore = new FakeAuthMethodStore(BitbucketAuthMethod.AppPassword);
        var tokenProvider = new FakeTokenProvider();
        var service = new BitbucketOAuthConnectionService(methodStore, () => tokenProvider, new FakeTokenStore());

        await service.ConnectAsync(CancellationToken.None);

        Assert.Equal(BitbucketAuthMethod.OAuth2, methodStore.SavedMethod);
        Assert.Equal(1, tokenProvider.GetValidTokenCallCount);
    }

    [Fact]
    public async Task ConnectAsync_WhenAuthorizationFails_DoesNotChangeSelectedMethod()
    {
        var methodStore = new FakeAuthMethodStore(BitbucketAuthMethod.AppPassword);
        var service = new BitbucketOAuthConnectionService(
            methodStore,
            () => new FakeTokenProvider(new BitbucketAuthenticationException("Acceso denegado.")),
            new FakeTokenStore());

        await Assert.ThrowsAsync<BitbucketAuthenticationException>(() => service.ConnectAsync(CancellationToken.None));

        Assert.Equal(BitbucketAuthMethod.AppPassword, methodStore.SavedMethod);
    }

    [Fact]
    public async Task IsConnectedAsync_WhenOAuthAndTokenAreStored_ReturnsTrue()
    {
        var service = new BitbucketOAuthConnectionService(
            new FakeAuthMethodStore(BitbucketAuthMethod.OAuth2),
            () => new FakeTokenProvider(),
            new FakeTokenStore(CreateToken()));

        var result = await service.IsConnectedAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsConnectedAsync_WhenTheTokenIsMissing_ReturnsFalse()
    {
        var service = new BitbucketOAuthConnectionService(
            new FakeAuthMethodStore(BitbucketAuthMethod.OAuth2),
            () => new FakeTokenProvider(),
            new FakeTokenStore());

        var result = await service.IsConnectedAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task DisconnectAsync_DeletesTokenAndClearsSelectedMethod()
    {
        var methodStore = new FakeAuthMethodStore(BitbucketAuthMethod.OAuth2);
        var tokenStore = new FakeTokenStore(CreateToken());
        var service = new BitbucketOAuthConnectionService(methodStore, () => new FakeTokenProvider(), tokenStore);

        await service.DisconnectAsync(CancellationToken.None);

        Assert.Null(methodStore.SavedMethod);
        Assert.Null(await tokenStore.GetAsync(CancellationToken.None));
    }

    private static OAuthToken CreateToken() =>
        new("access", "refresh", "Bearer", DateTimeOffset.UtcNow.AddHours(1));

    private sealed class FakeAuthMethodStore : IBitbucketAuthMethodStore
    {
        public FakeAuthMethodStore(BitbucketAuthMethod? savedMethod = null)
        {
            SavedMethod = savedMethod;
        }

        public BitbucketAuthMethod? SavedMethod { get; private set; }

        public Task<BitbucketAuthMethod?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SavedMethod);

        public Task SaveAsync(BitbucketAuthMethod method, CancellationToken cancellationToken)
        {
            SavedMethod = method;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            SavedMethod = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokenStore : IOAuthTokenStore
    {
        private OAuthToken? _token;

        public FakeTokenStore(OAuthToken? token = null)
        {
            _token = token;
        }

        public Task<OAuthToken?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_token);

        public Task SaveAsync(OAuthToken token, CancellationToken cancellationToken)
        {
            _token = token;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            _token = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokenProvider : IBitbucketOAuthTokenProvider
    {
        private readonly Exception? _exception;

        public FakeTokenProvider(Exception? exception = null)
        {
            _exception = exception;
        }

        public int GetValidTokenCallCount { get; private set; }

        public Task<OAuthToken> GetValidTokenAsync(CancellationToken cancellationToken)
        {
            GetValidTokenCallCount++;
            if (_exception is not null)
            {
                return Task.FromException<OAuthToken>(_exception);
            }

            return Task.FromResult(new OAuthToken("access", "refresh", "Bearer", DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task InvalidateCurrentTokenAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
