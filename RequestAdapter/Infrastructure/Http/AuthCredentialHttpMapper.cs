using System.Net.Http.Headers;
using System.Text;
using RequestAdapter.Domain.Auth;

namespace RequestAdapter.Infrastructure.Http;

internal static class AuthCredentialHttpMapper
{
    public static void Apply(HttpRequestMessage httpRequestMessage, AuthCredential credential)
    {
        httpRequestMessage.Headers.Authorization = credential switch
        {
            BasicAuthCredential basic => new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.Identifier}:{basic.Secret}"))),
            BearerTokenCredential bearer => new AuthenticationHeaderValue("Bearer", bearer.Token),
            _ => throw new NotSupportedException($"Credential type '{credential.GetType().Name}' is not supported.")
        };
    }
}
