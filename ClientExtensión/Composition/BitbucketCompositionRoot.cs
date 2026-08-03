using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Abstractions.AppPassword;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Application.Auth;
using BitbucketAdapter.Application.Connection;
using BitbucketAdapter.Application.Auth.AppPassword;
using BitbucketAdapter.Application.Auth.OAuth;
using BitbucketAdapter.Infrastructure.Auth;
using BitbucketAdapter.Infrastructure.Auth.AppPassword;
using BitbucketAdapter.Infrastructure.Auth.OAuth;
using BitbucketAdapter.Infrastructure.Workspaces;
using ClientExtension.Composition.AppPassword;
using ClientExtension.Composition.OAuth;
using ClientExtension.Presentation.Bitbucket.Authentication;
using ClientExtension.Presentation.Bitbucket.PullRequests;
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
        services.AddSingleton<IOAuthTokenStore>(_ => new DpapiOAuthTokenStore());
        services.AddSingleton<Func<IBitbucketOAuthTokenProvider>>(provider =>
            () => BuildOAuthTokenProvider(provider.GetRequiredService<IOAuthTokenStore>()));
        services.AddSingleton<IBitbucketOAuthConnectionService>(provider => new BitbucketOAuthConnectionService(
            provider.GetRequiredService<IBitbucketAuthMethodStore>(),
            provider.GetRequiredService<Func<IBitbucketOAuthTokenProvider>>(),
            provider.GetRequiredService<IOAuthTokenStore>()));
        services.AddSingleton<BitbucketAuthenticationCommandVisibility>();
        services.AddSingleton<IBitbucketConnectionStatusService, BitbucketConnectionStatusService>();
        services.AddSingleton<IBitbucketWorkspaceSelectionStore>(_ => new FileBitbucketWorkspaceSelectionStore());
        services.AddSingleton<IBitbucketWorkspaceService, BitbucketWorkspaceService>();
        services.AddSingleton<PullRequestSelectionData>();
        services.AddSingleton<IHttpRequestSender>(provider => BuildSender(
            provider.GetRequiredService<VisualStudioExtensibility>(),
            provider.GetRequiredService<IBitbucketAuthMethodStore>(),
            provider.GetRequiredService<Func<IBitbucketOAuthTokenProvider>>()));
        return services;
    }

    private static IHttpRequestSender BuildSender(
        VisualStudioExtensibility extensibility,
        IBitbucketAuthMethodStore authMethodStore,
        Func<IBitbucketOAuthTokenProvider> oAuthTokenProviderFactory)
    {
        var httpClient = new HttpClient();
        IHttpRequestSender rawSender = new HttpClientRequestSender(httpClient);

        IAppPasswordCredentialStore credentialStore = new DpapiAppPasswordCredentialStore();
        IAppPasswordPrompt credentialPrompt = new BitbucketAppPasswordPrompt(extensibility);
        var appPasswordProvider = new BitbucketAppPasswordProvider(credentialStore, credentialPrompt);

        var credentialProvider = new BitbucketCredentialProvider(
            authMethodStore,
            appPasswordProvider,
            oAuthTokenProviderFactory);
        var authenticationProvider = new BitbucketAuthenticationProvider(credentialProvider);

        IHttpRequestSender authenticatedSender = new AuthenticatedHttpRequestSender(rawSender, authenticationProvider);
        return new UnauthorizedRetryHttpRequestSender(authenticatedSender, credentialProvider);
    }

    private static IBitbucketOAuthTokenProvider BuildOAuthTokenProvider(IOAuthTokenStore tokenStore)
    {
        var consumer = BitbucketOAuthConsumerSecrets.Load();
        IHttpRequestSender rawSender = new HttpClientRequestSender(new HttpClient());
        var tokenExchangeClient = new BitbucketOAuthTokenExchangeClient(rawSender, consumer);
        IAuthorizationCodeProvider authorizationCodeProvider = new BitbucketLoopbackAuthorizationCodeProvider(consumer);

        return new BitbucketOAuthTokenProvider(tokenStore, tokenExchangeClient, authorizationCodeProvider);
    }
}
