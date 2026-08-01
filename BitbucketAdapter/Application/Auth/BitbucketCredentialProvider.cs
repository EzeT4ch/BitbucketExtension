using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Auth.AppPassword;
using BitbucketAdapter.Application.Auth.OAuth;
using BitbucketAdapter.Domain.Auth;
using BitbucketAdapter.Domain.Exceptions;
using RequestAdapter.Domain.Auth;

namespace BitbucketAdapter.Application.Auth;

public sealed class BitbucketCredentialProvider
{
    private readonly IBitbucketAuthMethodStore _methodStore;
    private readonly BitbucketAppPasswordProvider _appPasswordProvider;
    private readonly Lazy<BitbucketOAuthTokenProvider> _oAuthTokenProvider;

    public BitbucketCredentialProvider(
        IBitbucketAuthMethodStore methodStore,
        BitbucketAppPasswordProvider appPasswordProvider,
        Func<BitbucketOAuthTokenProvider> oAuthTokenProviderFactory)
    {
        _methodStore = methodStore;
        _appPasswordProvider = appPasswordProvider;
        _oAuthTokenProvider = new Lazy<BitbucketOAuthTokenProvider>(oAuthTokenProviderFactory);
    }

    public async Task<AuthCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        var method = await _methodStore.GetAsync(cancellationToken);
        return method switch
        {
            BitbucketAuthMethod.AppPassword => await GetAppPasswordCredentialAsync(cancellationToken),
            BitbucketAuthMethod.OAuth2 => await GetOAuthCredentialAsync(cancellationToken),
            _ => throw new BitbucketAuthenticationException(
                "Todavía no se eligió un método de autenticación de Bitbucket. Ejecutá el comando " +
                "'Bitbucket: Usar App Password' o 'Bitbucket: Usar OAuth 2.0' antes de conectar."),
        };
    }

    public async Task InvalidateCurrentCredentialAsync(CancellationToken cancellationToken)
    {
        var method = await _methodStore.GetAsync(cancellationToken);
        switch (method)
        {
            case BitbucketAuthMethod.AppPassword:
                await _appPasswordProvider.InvalidateCurrentCredentialAsync(cancellationToken);
                break;
            case BitbucketAuthMethod.OAuth2:
                await _oAuthTokenProvider.Value.InvalidateCurrentTokenAsync(cancellationToken);
                break;
        }
    }

    private async Task<AuthCredential> GetAppPasswordCredentialAsync(CancellationToken cancellationToken)
    {
        var credential = await _appPasswordProvider.GetCredentialAsync(cancellationToken);
        return new BasicAuthCredential(credential.Username, credential.AppPassword);
    }

    private async Task<AuthCredential> GetOAuthCredentialAsync(CancellationToken cancellationToken)
    {
        var token = await _oAuthTokenProvider.Value.GetValidTokenAsync(cancellationToken);
        return new BearerTokenCredential(token.AccessToken);
    }
}
