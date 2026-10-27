using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.Repositories;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.Repositories;

public sealed class BitbucketRepositoryService : IBitbucketRepositoryService
{
    private static readonly Uri ApiBaseEndpoint = new("https://api.bitbucket.org/");
    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { ["Accept"] = "application/json" };

    private readonly IHttpRequestSender _requestSender;

    public BitbucketRepositoryService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<BitbucketRepository>> GetAllAsync(
        string workspaceSlug,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);

        var repositoriesBySlug = new Dictionary<string, BitbucketRepository>(StringComparer.OrdinalIgnoreCase);
        var visitedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? currentEndpoint = new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Uri.EscapeDataString(workspaceSlug.Trim())}?pagelen=100");

        while (currentEndpoint is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedEndpoints.Add(currentEndpoint.AbsoluteUri))
            {
                throw new BitbucketRepositoryException(
                    "La paginación de repositorios de Bitbucket contiene un enlace repetido.");
            }

            var request = new RequestMessage(HttpMethodType.Get, currentEndpoint, Headers: Headers);
            var response = await _requestSender.SendAsync(request, cancellationToken);
            var page = ParsePage(response);

            foreach (var value in page.Values)
            {
                if (string.IsNullOrWhiteSpace(value.Uuid) ||
                    string.IsNullOrWhiteSpace(value.Slug) ||
                    string.IsNullOrWhiteSpace(value.Name))
                {
                    throw new BitbucketRepositoryException(
                        "La respuesta de repositorios de Bitbucket contiene un repositorio inválido.");
                }

                var repository = new BitbucketRepository(
                    value.Uuid.Trim(),
                    value.Slug.Trim(),
                    value.Name.Trim());
                repositoriesBySlug.TryAdd(repository.Slug, repository);
            }

            currentEndpoint = ParseNextEndpoint(page.Next);
        }

        return repositoriesBySlug.Values
            .OrderBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(repository => repository.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ParsedRepositoryPage ParsePage(ResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketRepositoryException(
                $"Bitbucket no pudo obtener los repositorios. Status: {response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketRepositoryException("La respuesta de repositorios de Bitbucket vino vacía.");
        }

        try
        {
            var page = JsonSerializer.Deserialize<RepositoryPageDto>(response.Content);
            if (page?.Values is null)
            {
                throw new BitbucketRepositoryException(
                    "La respuesta de repositorios de Bitbucket no contiene una colección de resultados.");
            }

            return new ParsedRepositoryPage(page.Values, page.Next);
        }
        catch (JsonException exception)
        {
            throw new BitbucketRepositoryException(
                "No se pudo interpretar la respuesta de repositorios de Bitbucket.",
                exception);
        }
    }

    private static Uri? ParseNextEndpoint(string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return null;
        }

        if (!Uri.TryCreate(next, UriKind.Absolute, out var nextEndpoint) ||
            nextEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(nextEndpoint.Host, ApiBaseEndpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new BitbucketRepositoryException(
                "La respuesta de repositorios de Bitbucket contiene un enlace de paginación inválido.");
        }

        return nextEndpoint;
    }

    private sealed record RepositoryPageDto(
        [property: JsonPropertyName("values")] List<RepositoryDto>? Values,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record ParsedRepositoryPage(
        IReadOnlyList<RepositoryDto> Values,
        string? Next);

    private sealed record RepositoryDto(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("name")] string? Name);
}
