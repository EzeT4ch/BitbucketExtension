using System.Text.Json.Serialization;
using BitbucketAdapter.Domain.Auth.OAuth;

namespace BitbucketAdapter.Infrastructure.Auth.OAuth;

internal static class OAuthTokenResponseMapper
{
    public static OAuthToken ToOAuthToken(TokenResponseDto dto, DateTimeOffset receivedAtUtc)
    {
        return new OAuthToken(
            dto.AccessToken,
            dto.RefreshToken,
            dto.TokenType,
            receivedAtUtc.AddSeconds(dto.ExpiresIn));
    }

    internal sealed record TokenResponseDto(
        [property: JsonPropertyName("access_token")]
        string AccessToken,
        [property: JsonPropertyName("refresh_token")]
        string? RefreshToken,
        [property: JsonPropertyName("token_type")]
        string TokenType,
        [property: JsonPropertyName("expires_in")]
        long ExpiresIn);
}