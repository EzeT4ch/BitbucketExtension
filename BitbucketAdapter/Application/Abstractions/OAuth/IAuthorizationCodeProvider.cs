namespace BitbucketAdapter.Application.Abstractions.OAuth;

public interface IAuthorizationCodeProvider
{
    Task<string> GetAuthorizationCodeAsync(CancellationToken cancellationToken);
}
