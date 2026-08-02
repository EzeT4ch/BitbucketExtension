namespace BitbucketAdapter.Application.Abstractions;

public interface IBitbucketConnectionStatusService
{
    Task<BitbucketConnectionStatus> GetAsync(CancellationToken cancellationToken);
}

public sealed record BitbucketConnectionStatus(int StatusCode);
