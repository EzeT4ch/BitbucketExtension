namespace BitbucketAdapter.Domain.Auth.OAuth;

public sealed record OAuthToken(
    string AccessToken,
    string? RefreshToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc)
{
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(60);

    public bool IsExpired(DateTimeOffset asOf) => asOf >= ExpiresAtUtc - ExpirySafetyMargin;
}
