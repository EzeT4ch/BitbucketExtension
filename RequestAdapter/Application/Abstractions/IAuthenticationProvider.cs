using RequestAdapter.Domain.Auth;

namespace RequestAdapter.Application.Abstractions;

public interface IAuthenticationProvider
{
    Task<AuthCredential> GetCredentialAsync(CancellationToken cancellationToken);
}
