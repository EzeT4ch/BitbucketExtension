using BitbucketAdapter.Domain.Auth;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketAuthMethodStore
{
    Task<BitbucketAuthMethod?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(BitbucketAuthMethod method, CancellationToken cancellationToken);
}
