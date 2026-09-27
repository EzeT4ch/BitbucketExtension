using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.Users;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.Users;

public sealed class BitbucketCurrentUserService : IBitbucketCurrentUserService
{
    private static readonly Uri CurrentUserEndpoint = new("https://api.bitbucket.org/2.0/user");
    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { ["Accept"] = "application/json" };

    private readonly IHttpRequestSender _requestSender;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private BitbucketUser? _currentUser;

    public BitbucketCurrentUserService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<BitbucketUser> GetAsync(CancellationToken cancellationToken)
    {
        if (_currentUser is not null)
        {
            return _currentUser;
        }

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_currentUser is not null)
            {
                return _currentUser;
            }

            var request = new RequestMessage(HttpMethodType.Get, CurrentUserEndpoint, Headers: Headers);
            var response = await _requestSender.SendAsync(request, cancellationToken);
            _currentUser = ParseUser(response);
            return _currentUser;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    private static BitbucketUser ParseUser(ResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketUserException(
                $"Bitbucket no pudo obtener el usuario actual. Status: {response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketUserException("La respuesta del usuario actual de Bitbucket vino vacía.");
        }

        try
        {
            var user = JsonSerializer.Deserialize<UserDto>(response.Content);
            if (string.IsNullOrWhiteSpace(user?.Uuid))
            {
                throw new BitbucketUserException(
                    "La respuesta del usuario actual de Bitbucket no contiene un identificador.");
            }

            return new BitbucketUser(
                user.Uuid.Trim(),
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Uuid.Trim() : user.DisplayName.Trim());
        }
        catch (JsonException exception)
        {
            throw new BitbucketUserException(
                "No se pudo interpretar la respuesta del usuario actual de Bitbucket.",
                exception);
        }
    }

    private sealed record UserDto(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("display_name")] string? DisplayName);
}
