using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Abstractions.AppPassword;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Application.Auth;
using BitbucketAdapter.Application.Auth.AppPassword;
using BitbucketAdapter.Application.Auth.OAuth;
using BitbucketAdapter.Infrastructure.Auth;
using BitbucketAdapter.Infrastructure.Auth.AppPassword;
using BitbucketAdapter.Infrastructure.Auth.OAuth;
using ClientExtension.Composition.AppPassword;
using ClientExtension.Composition.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Infrastructure.Http;

namespace ClientExtension.Composition;

internal static class BitbucketCompositionRoot
{
    public static IServiceCollection AddBitbucketServices(this IServiceCollection services)
    {
        services.AddSingleton<IBitbucketAuthMethodStore>(_ => new FileBitbucketAuthMethodStore());
        services.AddSingleton<IHttpRequestSender>(provider => BuildSender(
            provider.GetRequiredService<VisualStudioExtensibility>(),
            provider.GetRequiredService<IBitbucketAuthMethodStore>()));
        return services;
    }

    private static IHttpRequestSender BuildSender(VisualStudioExtensibility extensibility, IBitbucketAuthMethodStore authMethodStore)
    {
        var httpClient = new HttpClient();
        IHttpRequestSender rawSender = new HttpClientRequestSender(httpClient);

        IAppPasswordCredentialStore credentialStore = new DpapiAppPasswordCredentialStore();
        IAppPasswordPrompt credentialPrompt = new BitbucketAppPasswordPrompt(extensibility);
        var appPasswordProvider = new BitbucketAppPasswordProvider(credentialStore, credentialPrompt);

        var credentialProvider = new BitbucketCredentialProvider(
            authMethodStore,
            appPasswordProvider,
            () => BuildOAuthTokenProvider(rawSender));
        var authenticationProvider = new BitbucketAuthenticationProvider(credentialProvider);

        IHttpRequestSender authenticatedSender = new AuthenticatedHttpRequestSender(rawSender, authenticationProvider);
        return new UnauthorizedRetryHttpRequestSender(authenticatedSender, credentialProvider);
    }

    private static BitbucketOAuthTokenProvider BuildOAuthTokenProvider(IHttpRequestSender rawSender)
    {
        var consumer = BitbucketOAuthConsumerSecrets.Load();
        var tokenExchangeClient = new BitbucketOAuthTokenExchangeClient(rawSender, consumer);
        IOAuthTokenStore tokenStore = new DpapiOAuthTokenStore();
        IAuthorizationCodeProvider authorizationCodeProvider = new BitbucketLoopbackAuthorizationCodeProvider(consumer);

        return new BitbucketOAuthTokenProvider(tokenStore, tokenExchangeClient, authorizationCodeProvider);
    }
}
