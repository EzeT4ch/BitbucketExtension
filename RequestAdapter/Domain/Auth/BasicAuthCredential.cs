namespace RequestAdapter.Domain.Auth;

public sealed record BasicAuthCredential(string Identifier, string Secret) : AuthCredential;
