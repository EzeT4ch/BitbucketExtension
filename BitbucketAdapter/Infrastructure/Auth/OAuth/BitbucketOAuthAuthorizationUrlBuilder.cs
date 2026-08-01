using BitbucketAdapter.Domain.Auth.OAuth;

namespace BitbucketAdapter.Infrastructure.Auth.OAuth;

public static class BitbucketOAuthAuthorizationUrlBuilder
{
    private static readonly Uri AuthorizationEndpoint = new("https://bitbucket.org/site/oauth2/authorize");

    public static Uri Build(OAuthConsumer consumer, string state)
    {
        var query = string.Join(
            '&',
            $"client_id={Uri.EscapeDataString(consumer.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(consumer.RedirectUri)}",
            $"state={Uri.EscapeDataString(state)}");

        return new UriBuilder(AuthorizationEndpoint) { Query = query }.Uri;
    }
}
