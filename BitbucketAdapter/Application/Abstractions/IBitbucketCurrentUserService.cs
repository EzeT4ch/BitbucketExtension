using BitbucketAdapter.Domain.Users;

namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketCurrentUserService
{
    Task<BitbucketUser> GetAsync(CancellationToken cancellationToken);
}
