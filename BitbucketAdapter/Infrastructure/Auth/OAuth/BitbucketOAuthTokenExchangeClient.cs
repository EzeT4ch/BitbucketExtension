using System.Text.Json;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Domain.Exceptions;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.Auth.OAuth;

public sealed class BitbucketOAuthTokenExchangeClient : IOAuthTokenExchangeClient
{
    private static readonly Uri TokenEndpoint = new("https://bitbucket.org/site/oauth2/access_token");

    private readonly IHttpRequestSender _rawSender;
    private readonly OAuthConsumer _consumer;

    public BitbucketOAuthTokenExchangeClient(IHttpRequestSender rawSender, OAuthConsumer consumer)
    {
        _rawSender = rawSender;
        _consumer = consumer;
    }

    public Task<OAuthToken> ExchangeAuthorizationCodeAsync(string authorizationCode,
        CancellationToken cancellationToken)
    {
        var body = string.Join(
            '&',
            "grant_type=authorization_code",
            $"code={Uri.EscapeDataString(authorizationCode)}",
            $"redirect_uri={Uri.EscapeDataString(_consumer.RedirectUri)}");
        return SendTokenRequestAsync(body, cancellationToken);
    }

    public Task<OAuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var body = $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refreshToken)}";
        return SendTokenRequestAsync(body, cancellationToken);
    }

    private async Task<OAuthToken> SendTokenRequestAsync(string formUrlEncodedBody, CancellationToken cancellationToken)
    {
        var request = new RequestMessage(
            HttpMethodType.Post,
            TokenEndpoint,
            Body: new RequestBody("application/x-www-form-urlencoded", formUrlEncodedBody),
            Credential: new BasicAuthCredential(_consumer.ClientId, _consumer.ClientSecret));

        var requestSentAtUtc = DateTimeOffset.UtcNow;
        var response = await _rawSender.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketAuthenticationException(
                $"El intercambio de token con Bitbucket falló con status {response.StatusCode}: {response.Content}");
        }

        if (string.IsNullOrEmpty(response.Content))
        {
            throw new BitbucketAuthenticationException("La respuesta del endpoint de token de Bitbucket vino vacía.");
        }

        OAuthTokenResponseMapper.TokenResponseDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<OAuthTokenResponseMapper.TokenResponseDto>(response.Content)
                  ?? throw new BitbucketAuthenticationException("La respuesta del endpoint de token de Bitbucket vino vacía.");
        }
        catch (JsonException exception)
        {
            throw new BitbucketAuthenticationException("No se pudo interpretar la respuesta del endpoint de token de Bitbucket.",
                exception);
        }

        return OAuthTokenResponseMapper.ToOAuthToken(dto, requestSentAtUtc);
    }
}
