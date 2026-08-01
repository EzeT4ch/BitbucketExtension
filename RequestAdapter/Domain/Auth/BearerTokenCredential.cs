namespace RequestAdapter.Domain.Auth;

public sealed record BearerTokenCredential(string Token) : AuthCredential;
