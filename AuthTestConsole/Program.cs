using AuthTestConsole;
using BitbucketAdapter.Application.Auth;
using BitbucketAdapter.Application.Auth.AppPassword;
using BitbucketAdapter.Application.Auth.OAuth;
using BitbucketAdapter.Domain.Auth;
using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Infrastructure.Auth;
using BitbucketAdapter.Infrastructure.Auth.AppPassword;
using BitbucketAdapter.Infrastructure.Auth.OAuth;
using Microsoft.Extensions.Configuration;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Requests;
using RequestAdapter.Infrastructure.Http;

const string UserSecretsId = "7b6b6a5e-9a2f-4d3a-9a4e-3c9e2f6a4b1a";

Console.WriteLine("1) App Password");
Console.WriteLine("2) OAuth 2.0");
Console.Write("Elegí un método: ");
var choice = Console.ReadLine();

var httpClient = new HttpClient();
var rawSender = new HttpClientRequestSender(httpClient);

BitbucketCredentialProvider credentialProvider;

if (choice == "2")
{
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets(UserSecretsId)
        .AddEnvironmentVariables("BITBUCKET_OAUTH_")
        .Build();

    var clientId = configuration["ClientId"];
    var clientSecret = configuration["ClientSecret"];
    var redirectUri = configuration["RedirectUri"];

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        Console.WriteLine("Falta ClientId/ClientSecret. Corré 'dotnet user-secrets set' en ClientExtensión (mismo UserSecretsId) o definí BITBUCKET_OAUTH_ClientId/BITBUCKET_OAUTH_ClientSecret.");
        return;
    }

    var consumer = new OAuthConsumer(clientId, clientSecret, string.IsNullOrWhiteSpace(redirectUri) ? "http://localhost:12345/callback" : redirectUri);
    var tokenExchangeClient = new BitbucketOAuthTokenExchangeClient(rawSender, consumer);
    var tokenStore = new DpapiOAuthTokenStore();
    var authorizationCodeProvider = new ConsoleAuthorizationCodeProvider(consumer);
    var oAuthTokenProvider = new BitbucketOAuthTokenProvider(tokenStore, tokenExchangeClient, authorizationCodeProvider);

    credentialProvider = new BitbucketCredentialProvider(
        new FixedBitbucketAuthMethodStore(BitbucketAuthMethod.OAuth2),
        new BitbucketAppPasswordProvider(new DpapiAppPasswordCredentialStore(), new ConsoleAppPasswordPrompt()),
        () => oAuthTokenProvider);
}
else
{
    var appPasswordProvider = new BitbucketAppPasswordProvider(new DpapiAppPasswordCredentialStore(), new ConsoleAppPasswordPrompt());

    credentialProvider = new BitbucketCredentialProvider(
        new FixedBitbucketAuthMethodStore(BitbucketAuthMethod.AppPassword),
        appPasswordProvider,
        () => throw new InvalidOperationException("OAuth no configurado en este harness."));
}

var authenticationProvider = new BitbucketAuthenticationProvider(credentialProvider);
var authenticatedSender = new AuthenticatedHttpRequestSender(rawSender, authenticationProvider);
var sender = new UnauthorizedRetryHttpRequestSender(authenticatedSender, credentialProvider);

var request = new RequestMessage(HttpMethodType.Get, new Uri("https://api.bitbucket.org/2.0/user"));
var response = await sender.SendAsync(request, CancellationToken.None);

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(response.Content);
