using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.PullRequests;

public sealed class BitbucketPullRequestCommentService : IBitbucketPullRequestCommentService
{
    private static readonly Uri ApiBaseEndpoint = new("https://api.bitbucket.org/");
    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { ["Accept"] = "application/json" };

    private readonly IHttpRequestSender _requestSender;

    public BitbucketPullRequestCommentService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<BitbucketPullRequestComment>> GetAllAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        if (pullRequestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pullRequestId));
        }

        var commentsById = await GetCommentsByIdAsync(
            BuildCommentsEndpoint(workspaceSlug, repositorySlug, pullRequestId),
            cancellationToken);

        return commentsById.Values
            .Select(comment => MapComment(comment, commentsById))
            .OrderBy(comment => comment.CreatedOn ?? DateTimeOffset.MinValue)
            .ThenBy(comment => comment.Id)
            .ToArray();
    }

    public async Task<IReadOnlyList<BitbucketPullRequestComment>> GetUnresolvedForFileAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (pullRequestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pullRequestId));
        }

        var normalizedFilePath = NormalizePath(filePath);
        var commentsById = await GetCommentsByIdAsync(
            BuildCommentsEndpoint(workspaceSlug, repositorySlug, pullRequestId),
            cancellationToken);

        return commentsById.Values
            .Select(comment => MapComment(comment, commentsById))
            .Where(comment =>
                !comment.IsDeleted &&
                !comment.IsPending &&
                !comment.IsResolved &&
                comment.ParentId is null &&
                (comment.FromLine is not null || comment.ToLine is not null))
            .Where(comment => string.Equals(
                NormalizePath(comment.FilePath ?? string.Empty),
                normalizedFilePath,
                StringComparison.Ordinal))
            .OrderBy(comment => comment.ToLine ?? comment.FromLine ?? int.MaxValue)
            .ThenBy(comment => comment.CreatedOn ?? DateTimeOffset.MinValue)
            .ThenBy(comment => comment.Id)
            .ToArray();
    }

    private async Task<Dictionary<long, PullRequestCommentDto>> GetCommentsByIdAsync(
        Uri initialEndpoint,
        CancellationToken cancellationToken)
    {
        var commentsById = new Dictionary<long, PullRequestCommentDto>();
        var visitedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? currentEndpoint = initialEndpoint;

        while (currentEndpoint is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedEndpoints.Add(currentEndpoint.AbsoluteUri))
            {
                throw new BitbucketPullRequestCommentException(
                    "La paginación de comentarios de Bitbucket contiene un enlace repetido.");
            }

            var request = new RequestMessage(HttpMethodType.Get, currentEndpoint, Headers: Headers);
            var response = await _requestSender.SendAsync(request, cancellationToken);
            var page = ParsePage(response);

            foreach (var comment in page.Values)
            {
                if (comment.Id is null or <= 0 ||
                    comment.Parent is not null && comment.Parent.Id is null or <= 0)
                {
                    throw new BitbucketPullRequestCommentException(
                        "La respuesta de Bitbucket contiene un comentario inválido.");
                }

                commentsById.TryAdd(comment.Id.Value, comment);
            }

            currentEndpoint = ParseNextEndpoint(page.Next);
        }

        return commentsById;
    }

    private static PullRequestCommentPage ParsePage(ResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestCommentException(
                $"Bitbucket no pudo obtener los comentarios del Pull Request. Status: {response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketPullRequestCommentException(
                "La respuesta de comentarios del Pull Request vino vacía.");
        }

        try
        {
            var page = JsonSerializer.Deserialize<PullRequestCommentPageDto>(response.Content);
            if (page?.Values is null)
            {
                throw new BitbucketPullRequestCommentException(
                    "La respuesta de Bitbucket no contiene una colección de comentarios.");
            }

            return new PullRequestCommentPage(page.Values, page.Next);
        }
        catch (JsonException exception)
        {
            throw new BitbucketPullRequestCommentException(
                "No se pudo interpretar la respuesta de comentarios de Bitbucket.",
                exception);
        }
    }

    private static BitbucketPullRequestComment MapComment(
        PullRequestCommentDto comment,
        IReadOnlyDictionary<long, PullRequestCommentDto> commentsById)
    {
        var isDeleted = comment.Deleted ?? false;
        var content = isDeleted
            ? "Comentario eliminado."
            : string.IsNullOrWhiteSpace(comment.Content?.Raw)
                ? "Sin contenido."
                : comment.Content.Raw.Trim();

        return new BitbucketPullRequestComment(
            comment.Id!.Value,
            comment.Parent?.Id,
            content,
            string.IsNullOrWhiteSpace(comment.User?.DisplayName)
                ? "Autor desconocido"
                : comment.User.DisplayName.Trim(),
            comment.CreatedOn,
            comment.UpdatedOn,
            isDeleted,
            comment.Pending ?? false,
            IsThreadResolved(comment, commentsById),
            comment.Inline?.Path?.Trim(),
            comment.Inline?.From,
            comment.Inline?.To);
    }

    private static bool IsThreadResolved(
        PullRequestCommentDto comment,
        IReadOnlyDictionary<long, PullRequestCommentDto> commentsById)
    {
        var visitedCommentIds = new HashSet<long>();
        var currentComment = comment;

        while (true)
        {
            if (currentComment.Resolution is not null)
            {
                return true;
            }

            var parentId = currentComment.Parent?.Id;
            if (parentId is null || !commentsById.TryGetValue(parentId.Value, out currentComment))
            {
                return false;
            }

            if (!visitedCommentIds.Add(parentId.Value))
            {
                throw new BitbucketPullRequestCommentException(
                    "La respuesta de Bitbucket contiene una relación circular entre comentarios.");
            }
        }
    }

    private static Uri BuildCommentsEndpoint(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId) =>
        new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Escape(workspaceSlug)}/{Escape(repositorySlug)}/pullrequests/{pullRequestId.ToString(CultureInfo.InvariantCulture)}/comments");

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
            throw new BitbucketPullRequestCommentException(
                "La respuesta de comentarios contiene un enlace de paginación inválido.");
        }

        return nextEndpoint;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/').Trim().Trim('/');

    private sealed record PullRequestCommentPageDto(
        [property: JsonPropertyName("values")] List<PullRequestCommentDto>? Values,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record PullRequestCommentPage(
        IReadOnlyList<PullRequestCommentDto> Values,
        string? Next);

    private sealed record PullRequestCommentDto(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("parent")] ParentDto? Parent,
        [property: JsonPropertyName("content")] ContentDto? Content,
        [property: JsonPropertyName("inline")] InlineDto? Inline,
        [property: JsonPropertyName("created_on")] DateTimeOffset? CreatedOn,
        [property: JsonPropertyName("updated_on")] DateTimeOffset? UpdatedOn,
        [property: JsonPropertyName("user")] AccountDto? User,
        [property: JsonPropertyName("deleted")] bool? Deleted,
        [property: JsonPropertyName("pending")] bool? Pending,
        [property: JsonPropertyName("resolution")] ResolutionDto? Resolution);

    private sealed record ParentDto(
        [property: JsonPropertyName("id")] long? Id);

    private sealed record ContentDto(
        [property: JsonPropertyName("raw")] string? Raw);

    private sealed record InlineDto(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("from")] int? From,
        [property: JsonPropertyName("to")] int? To);

    private sealed record AccountDto(
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed class ResolutionDto
    {
    }
}
