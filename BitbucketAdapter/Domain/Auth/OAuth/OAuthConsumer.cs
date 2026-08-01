namespace BitbucketAdapter.Domain.Auth.OAuth;

public sealed record OAuthConsumer(string ClientId, string ClientSecret, string RedirectUri);
