using BitbucketAdapter.Application.Auth;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Auth;

namespace BitbucketAdapter.Infrastructure.Auth;

public sealed class BitbucketAuthenticationProvider : IAuthenticationProvider
{
    private readonly BitbucketCredentialProvider _credentialProvider;

    public BitbucketAuthenticationProvider(BitbucketCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    public Task<AuthCredential> GetCredentialAsync(CancellationToken cancellationToken) =>
        _credentialProvider.GetCredentialAsync(cancellationToken);
}
