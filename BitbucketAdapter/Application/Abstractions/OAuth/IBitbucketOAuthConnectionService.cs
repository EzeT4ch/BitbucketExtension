namespace BitbucketAdapter.Application.Abstractions.OAuth;

public interface IBitbucketOAuthConnectionService
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task<bool> IsConnectedAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
