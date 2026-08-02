using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Infrastructure.Auth.OAuth;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketOAuthTokenExchangeClientTests
{
    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_IncludesConfiguredRedirectUriInTokenRequest()
    {
        var sender = new CapturingRequestSender();
        var redirectUri = "http://localhost:12345/callback";
        var client = new BitbucketOAuthTokenExchangeClient(
            sender,
            new OAuthConsumer("client-id", "client-secret", redirectUri));

        await client.ExchangeAuthorizationCodeAsync("authorization-code", CancellationToken.None);

        Assert.Equal(
            "grant_type=authorization_code&code=authorization-code&redirect_uri=http%3A%2F%2Flocalhost%3A12345%2Fcallback",
            sender.Request!.Body!.Content);
    }

    private sealed class CapturingRequestSender : IHttpRequestSender
    {
        public RequestMessage? Request { get; private set; }

        public Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ResponseMessage(
                200,
                true,
                new Dictionary<string, string>(),
                "{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
        }
    }
}
