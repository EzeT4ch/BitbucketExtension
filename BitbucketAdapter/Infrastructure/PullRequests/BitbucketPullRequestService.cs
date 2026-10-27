using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.PullRequests;

public sealed class BitbucketPullRequestService : IBitbucketPullRequestService
{
    private static readonly Uri ApiBaseEndpoint = new("https://api.bitbucket.org/");
    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { ["Accept"] = "application/json" };

    private readonly IHttpRequestSender _requestSender;

    public BitbucketPullRequestService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<BitbucketPullRequestSummary>> GetOpenAsync(
        string workspaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken)
    {
        ValidateRepositoryContext(workspaceSlug, repositorySlug);

        var pullRequestsById = new Dictionary<long, BitbucketPullRequestSummary>();
        var visitedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? currentEndpoint = BuildPullRequestsEndpoint(workspaceSlug, repositorySlug);

        while (currentEndpoint is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedEndpoints.Add(currentEndpoint.AbsoluteUri))
            {
                throw new BitbucketPullRequestException(
                    "La paginación de Pull Requests de Bitbucket contiene un enlace repetido.");
            }

            var response = await SendGetAsync(currentEndpoint, cancellationToken);
            var page = ParsePage(response);

            foreach (var value in page.Values)
            {
                var pullRequest = MapSummary(value);
                pullRequestsById.TryAdd(pullRequest.Id, pullRequest);
            }

            currentEndpoint = ParseNextEndpoint(page.Next);
        }

        return pullRequestsById.Values
            .OrderByDescending(pullRequest => pullRequest.UpdatedOn)
            .ThenByDescending(pullRequest => pullRequest.Id)
            .ToArray();
    }

    public async Task<BitbucketPullRequestDetail> GetByIdAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken)
    {
        ValidateRepositoryContext(workspaceSlug, repositorySlug);
        if (pullRequestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pullRequestId));
        }

        var endpoint = BuildPullRequestDetailEndpoint(workspaceSlug, repositorySlug, pullRequestId);
        var response = await SendGetAsync(endpoint, cancellationToken);
        var dto = ParsePullRequest(response, "detalle");

        return new BitbucketPullRequestDetail(
            MapSummary(dto),
            FirstNotEmpty(dto.Description, dto.Rendered?.Description?.Raw),
            dto.Reason?.Trim() ?? string.Empty,
            dto.Reviewers?
                .Select(reviewer => reviewer.DisplayName?.Trim())
                .Where(displayName => !string.IsNullOrWhiteSpace(displayName))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(displayName => displayName, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [],
            dto.Source?.Commit?.Hash?.Trim() ?? string.Empty,
            dto.Destination?.Commit?.Hash?.Trim() ?? string.Empty,
            dto.CloseSourceBranch ?? false,
            dto.Links?.Html?.Href?.Trim());
    }

    private async Task<ResponseMessage> SendGetAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var request = new RequestMessage(HttpMethodType.Get, endpoint, Headers: Headers);
        return await _requestSender.SendAsync(request, cancellationToken);
    }

    private static PullRequestPage ParsePage(ResponseMessage response)
    {
        EnsureSuccessfulContent(response, "listado");

        try
        {
            var page = JsonSerializer.Deserialize<PullRequestPageDto>(response.Content!);
            if (page?.Values is null)
            {
                throw new BitbucketPullRequestException(
                    "La respuesta de Pull Requests de Bitbucket no contiene una colección de resultados.");
            }

            return new PullRequestPage(page.Values, page.Next);
        }
        catch (JsonException exception)
        {
            throw new BitbucketPullRequestException(
                "No se pudo interpretar el listado de Pull Requests de Bitbucket.",
                exception);
        }
    }

    private static PullRequestDto ParsePullRequest(ResponseMessage response, string operation)
    {
        EnsureSuccessfulContent(response, operation);

        try
        {
            return JsonSerializer.Deserialize<PullRequestDto>(response.Content!)
                ?? throw new BitbucketPullRequestException(
                    "La respuesta de detalle del Pull Request de Bitbucket es inválida.");
        }
        catch (JsonException exception)
        {
            throw new BitbucketPullRequestException(
                "No se pudo interpretar el detalle del Pull Request de Bitbucket.",
                exception);
        }
    }

    private static void EnsureSuccessfulContent(ResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestException(
                $"Bitbucket no pudo obtener el {operation} de Pull Requests. Status: {response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketPullRequestException(
                $"La respuesta del {operation} de Pull Requests de Bitbucket vino vacía.");
        }
    }

    private static BitbucketPullRequestSummary MapSummary(PullRequestDto dto)
    {
        if (dto.Id is null or <= 0 ||
            string.IsNullOrWhiteSpace(dto.Title) ||
            string.IsNullOrWhiteSpace(dto.State) ||
            string.IsNullOrWhiteSpace(dto.Source?.Branch?.Name) ||
            string.IsNullOrWhiteSpace(dto.Destination?.Branch?.Name))
        {
            throw new BitbucketPullRequestException(
                "La respuesta de Bitbucket contiene un Pull Request inválido.");
        }

        return new BitbucketPullRequestSummary(
            dto.Id.Value,
            dto.Title.Trim(),
            dto.State.Trim(),
            string.IsNullOrWhiteSpace(dto.Author?.DisplayName)
                ? "Autor desconocido"
                : dto.Author.DisplayName.Trim(),
            dto.Source.Branch.Name.Trim(),
            dto.Destination.Branch.Name.Trim(),
            dto.CreatedOn,
            dto.UpdatedOn,
            Math.Max(dto.CommentCount ?? 0, 0),
            Math.Max(dto.TaskCount ?? 0, 0),
            dto.Draft ?? false);
    }

    private static Uri BuildPullRequestsEndpoint(string workspaceSlug, string repositorySlug) =>
        new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Escape(workspaceSlug)}/{Escape(repositorySlug)}/pullrequests?state=OPEN&pagelen=50");

    private static Uri BuildPullRequestDetailEndpoint(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId) =>
        new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Escape(workspaceSlug)}/{Escape(repositorySlug)}/pullrequests/{pullRequestId.ToString(CultureInfo.InvariantCulture)}");

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
            throw new BitbucketPullRequestException(
                "La respuesta de Pull Requests de Bitbucket contiene un enlace de paginación inválido.");
        }

        return nextEndpoint;
    }

    private static void ValidateRepositoryContext(string workspaceSlug, string repositorySlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record PullRequestPageDto(
        [property: JsonPropertyName("values")] List<PullRequestDto>? Values,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record PullRequestPage(
        IReadOnlyList<PullRequestDto> Values,
        string? Next);

    private sealed record PullRequestDto(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("author")] AccountDto? Author,
        [property: JsonPropertyName("source")] BranchReferenceDto? Source,
        [property: JsonPropertyName("destination")] BranchReferenceDto? Destination,
        [property: JsonPropertyName("created_on")] DateTimeOffset? CreatedOn,
        [property: JsonPropertyName("updated_on")] DateTimeOffset? UpdatedOn,
        [property: JsonPropertyName("comment_count")] int? CommentCount,
        [property: JsonPropertyName("task_count")] int? TaskCount,
        [property: JsonPropertyName("draft")] bool? Draft,
        [property: JsonPropertyName("close_source_branch")] bool? CloseSourceBranch,
        [property: JsonPropertyName("reviewers")] List<AccountDto>? Reviewers,
        [property: JsonPropertyName("rendered")] RenderedDto? Rendered,
        [property: JsonPropertyName("links")] PullRequestLinksDto? Links);

    private sealed record AccountDto(
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record BranchReferenceDto(
        [property: JsonPropertyName("branch")] BranchDto? Branch,
        [property: JsonPropertyName("commit")] CommitDto? Commit);

    private sealed record BranchDto(
        [property: JsonPropertyName("name")] string? Name);

    private sealed record CommitDto(
        [property: JsonPropertyName("hash")] string? Hash);

    private sealed record RenderedDto(
        [property: JsonPropertyName("description")] RenderedContentDto? Description);

    private sealed record RenderedContentDto(
        [property: JsonPropertyName("raw")] string? Raw);

    private sealed record PullRequestLinksDto(
        [property: JsonPropertyName("html")] LinkDto? Html);

    private sealed record LinkDto(
        [property: JsonPropertyName("href")] string? Href);
}
