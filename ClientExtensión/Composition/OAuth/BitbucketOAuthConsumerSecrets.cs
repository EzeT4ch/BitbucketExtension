using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Domain.Exceptions;
using Microsoft.Extensions.Configuration;

namespace ClientExtension.Composition.OAuth;

internal static class BitbucketOAuthConsumerSecrets
{
    private const string DefaultRedirectUri = "http://localhost:12345/callback";

    public static OAuthConsumer Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<ClientExtensionEntrypoint>()
            .AddEnvironmentVariables("BITBUCKET_OAUTH_")
            .Build();

        var clientId = configuration["ClientId"];
        var clientSecret = configuration["ClientSecret"];
        var redirectUri = configuration["RedirectUri"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new BitbucketAuthenticationException(
                "Falta configurar el OAuth consumer de Bitbucket. Desde la carpeta ClientExtensión ejecutá: " +
                "dotnet user-secrets set \"ClientId\" \"<tu client id>\" y " +
                "dotnet user-secrets set \"ClientSecret\" \"<tu client secret>\" " +
                "(opcionalmente \"RedirectUri\" si no usás http://localhost:12345/callback).");
        }

        return new OAuthConsumer(clientId, clientSecret, string.IsNullOrWhiteSpace(redirectUri) ? DefaultRedirectUri : redirectUri);
    }
}
