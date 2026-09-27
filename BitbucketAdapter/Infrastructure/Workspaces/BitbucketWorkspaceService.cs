using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.Workspaces;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.Workspaces;

public sealed class BitbucketWorkspaceService : IBitbucketWorkspaceService
{
    private static readonly Uri WorkspacesEndpoint = new("https://api.bitbucket.org/2.0/user/workspaces?pagelen=100");
    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { ["Accept"] = "application/json" };

    private readonly IHttpRequestSender _requestSender;

    public BitbucketWorkspaceService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<BitbucketWorkspace>> GetAllAsync(CancellationToken cancellationToken)
    {
        var workspacesBySlug = new Dictionary<string, BitbucketWorkspace>(StringComparer.OrdinalIgnoreCase);
        var visitedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? currentEndpoint = WorkspacesEndpoint;

        while (currentEndpoint is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedEndpoints.Add(currentEndpoint.AbsoluteUri))
            {
                throw new BitbucketWorkspaceException(
                    "La paginación de workspaces de Bitbucket contiene un enlace repetido.");
            }

            var request = new RequestMessage(HttpMethodType.Get, currentEndpoint, Headers: Headers);
            var response = await _requestSender.SendAsync(request, cancellationToken);
            var page = ParsePage(response);

            foreach (var value in page.Values)
            {
                if (value.Workspace is null ||
                    string.IsNullOrWhiteSpace(value.Workspace.Uuid) ||
                    string.IsNullOrWhiteSpace(value.Workspace.Slug))
                {
                    throw new BitbucketWorkspaceException(
                        "La respuesta de workspaces de Bitbucket contiene un workspace inválido.");
                }

                var workspace = new BitbucketWorkspace(
                    value.Workspace.Uuid.Trim(),
                    value.Workspace.Slug.Trim());
                workspacesBySlug.TryAdd(workspace.Slug, workspace);
            }

            currentEndpoint = ParseNextEndpoint(page.Next);
        }

        return workspacesBySlug.Values
            .OrderBy(workspace => workspace.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ParsedWorkspacePage ParsePage(ResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketWorkspaceException(
                $"Bitbucket no pudo obtener los workspaces. Status: {response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketWorkspaceException("La respuesta de workspaces de Bitbucket vino vacía.");
        }

        try
        {
            var page = JsonSerializer.Deserialize<WorkspacePageDto>(response.Content);
            if (page?.Values is null)
            {
                throw new BitbucketWorkspaceException(
                    "La respuesta de workspaces de Bitbucket no contiene una colección de resultados.");
            }

            return new ParsedWorkspacePage(page.Values, page.Next);
        }
        catch (JsonException exception)
        {
            throw new BitbucketWorkspaceException(
                "No se pudo interpretar la respuesta de workspaces de Bitbucket.",
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
            !string.Equals(nextEndpoint.Host, WorkspacesEndpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new BitbucketWorkspaceException(
                "La respuesta de workspaces de Bitbucket contiene un enlace de paginación inválido.");
        }

        return nextEndpoint;
    }

    private sealed record WorkspacePageDto(
        [property: JsonPropertyName("values")] List<WorkspaceAccessDto>? Values,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record ParsedWorkspacePage(
        IReadOnlyList<WorkspaceAccessDto> Values,
        string? Next);

    private sealed record WorkspaceAccessDto(
        [property: JsonPropertyName("workspace")] WorkspaceDto? Workspace);

    private sealed record WorkspaceDto(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("slug")] string? Slug);
}
