using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Auth;

namespace AuthTestConsole;

internal sealed class FixedBitbucketAuthMethodStore : IBitbucketAuthMethodStore
{
    private readonly BitbucketAuthMethod _method;

    public FixedBitbucketAuthMethodStore(BitbucketAuthMethod method)
    {
        _method = method;
    }

    public Task<BitbucketAuthMethod?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<BitbucketAuthMethod?>(_method);

    public Task SaveAsync(BitbucketAuthMethod method, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
