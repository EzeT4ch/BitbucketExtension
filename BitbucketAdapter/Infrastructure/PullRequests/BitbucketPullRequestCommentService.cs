using System.Collections.Concurrent;
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

    private static readonly JsonSerializerOptions WriteSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan CommentsCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IHttpRequestSender _requestSender;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedComments> _commentsCache = new(StringComparer.Ordinal);

    public BitbucketPullRequestCommentService(IHttpRequestSender requestSender)
        : this(requestSender, TimeProvider.System)
    {
    }

    public BitbucketPullRequestCommentService(IHttpRequestSender requestSender, TimeProvider timeProvider)
    {
        _requestSender = requestSender;
        _timeProvider = timeProvider;
    }

    public async Task<BitbucketPullRequestCommentImage> GetImageAsync(
        string workspaceSlug,
        string repositorySlug,
        Uri imageUri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        ArgumentNullException.ThrowIfNull(imageUri);
        ValidateImageUri(workspaceSlug, repositorySlug, imageUri);

        var request = new RequestMessage(
            HttpMethodType.Get,
            imageUri,
            Headers: new Dictionary<string, string> { ["Accept"] = "image/*" },
            ResponseBodyMode: ResponseBodyMode.Binary);
        var response = await _requestSender.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestCommentException(
                $"Bitbucket no pudo descargar la imagen del comentario. Status: {response.StatusCode}.");
        }

        if (response.ContentBytes is not { Length: > 0 } content)
        {
            throw new BitbucketPullRequestCommentException(
                "La imagen del comentario descargada desde Bitbucket vino vacía.");
        }

        var contentType = DetectSupportedImageContentType(content);
        if (contentType is null)
        {
            throw new BitbucketPullRequestCommentException(
                "Bitbucket devolvió un formato de imagen no compatible.");
        }

        return new BitbucketPullRequestCommentImage(content, contentType);
    }

    public Task<long> CreateReplyAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long parentCommentId,
        string markdownContent,
        CancellationToken cancellationToken)
    {
        ValidatePullRequestContext(workspaceSlug, repositorySlug, pullRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownContent);
        ValidateCommentId(parentCommentId, nameof(parentCommentId));

        var body = JsonSerializer.Serialize(new CreateReplyRequestDto(
            new ContentDto(markdownContent),
            new ParentDto(parentCommentId)));
        return SendCreateAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            body,
            "Bitbucket no pudo crear la respuesta al comentario.",
            cancellationToken);
    }

    public Task<long> CreateAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string markdownContent,
        BitbucketPullRequestCommentAnchor? anchor,
        CancellationToken cancellationToken)
    {
        ValidatePullRequestContext(workspaceSlug, repositorySlug, pullRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownContent);

        InlineDto? inline = null;
        if (anchor is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(anchor.Path);
            if (anchor.FromLine is null && anchor.ToLine is null ||
                anchor.FromLine is <= 0 ||
                anchor.ToLine is <= 0)
            {
                throw new ArgumentException(
                    "El comentario en línea debe indicar una línea válida.",
                    nameof(anchor));
            }

            inline = new InlineDto(NormalizePath(anchor.Path), anchor.FromLine, anchor.ToLine);
        }

        var body = JsonSerializer.Serialize(
            new CreateCommentRequestDto(new ContentDto(markdownContent), inline),
            WriteSerializerOptions);
        return SendCreateAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            body,
            "Bitbucket no pudo crear el comentario.",
            cancellationToken);
    }

    public Task UpdateAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        string markdownContent,
        CancellationToken cancellationToken)
    {
        ValidatePullRequestContext(workspaceSlug, repositorySlug, pullRequestId);
        ValidateCommentId(commentId, nameof(commentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownContent);

        var body = JsonSerializer.Serialize(new UpdateCommentRequestDto(new ContentDto(markdownContent)));
        return SendWriteAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            HttpMethodType.Put,
            BuildCommentEndpoint(workspaceSlug, repositorySlug, pullRequestId, commentId),
            body,
            "Bitbucket no pudo editar el comentario.",
            cancellationToken);
    }

    public Task DeleteAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        CancellationToken cancellationToken)
    {
        ValidatePullRequestContext(workspaceSlug, repositorySlug, pullRequestId);
        ValidateCommentId(commentId, nameof(commentId));

        return SendWriteAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            HttpMethodType.Delete,
            BuildCommentEndpoint(workspaceSlug, repositorySlug, pullRequestId, commentId),
            null,
            "Bitbucket no pudo eliminar el comentario.",
            cancellationToken);
    }

    public Task ResolveAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        CancellationToken cancellationToken)
    {
        ValidatePullRequestContext(workspaceSlug, repositorySlug, pullRequestId);
        ValidateCommentId(commentId, nameof(commentId));

        return SendWriteAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            HttpMethodType.Post,
            BuildResolveEndpoint(workspaceSlug, repositorySlug, pullRequestId, commentId),
            null,
            "Bitbucket no pudo resolver el comentario.",
            cancellationToken);
    }

    public Task ReopenAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId,
        CancellationToken cancellationToken)
    {
        ValidatePullRequestContext(workspaceSlug, repositorySlug, pullRequestId);
        ValidateCommentId(commentId, nameof(commentId));

        return SendWriteAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            HttpMethodType.Delete,
            BuildResolveEndpoint(workspaceSlug, repositorySlug, pullRequestId, commentId),
            null,
            "Bitbucket no pudo reabrir el comentario.",
            cancellationToken);
    }

    private async Task<long> SendCreateAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        string body,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var response = await SendWriteRequestAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            HttpMethodType.Post,
            BuildCommentsEndpoint(workspaceSlug, repositorySlug, pullRequestId),
            body,
            errorMessage,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketPullRequestCommentException(
                "La respuesta de Bitbucket al crear el comentario vino vacía.");
        }

        try
        {
            var createdComment = JsonSerializer.Deserialize<CreateReplyResponseDto>(response.Content);
            if (createdComment?.Id is null or <= 0)
            {
                throw new BitbucketPullRequestCommentException(
                    "La respuesta de Bitbucket no contiene un identificador de comentario válido.");
            }

            return createdComment.Id.Value;
        }
        catch (JsonException exception)
        {
            throw new BitbucketPullRequestCommentException(
                "No se pudo interpretar la respuesta de Bitbucket al crear el comentario.",
                exception);
        }
    }

    private async Task SendWriteAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        HttpMethodType method,
        Uri endpoint,
        string? body,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await SendWriteRequestAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            method,
            endpoint,
            body,
            errorMessage,
            cancellationToken);
    }

    private async Task<ResponseMessage> SendWriteRequestAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        HttpMethodType method,
        Uri endpoint,
        string? body,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var request = new RequestMessage(
            method,
            endpoint,
            Headers: Headers,
            Body: body is null ? null : new RequestBody("application/json", body));
        var response = await _requestSender.SendAsync(request, cancellationToken);
        _commentsCache.TryRemove(BuildCacheKey(workspaceSlug, repositorySlug, pullRequestId), out _);

        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestCommentException($"{errorMessage} Status: {response.StatusCode}.");
        }

        return response;
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

        var commentsById = await GetCachedCommentsByIdAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
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
        var commentsById = await GetCachedCommentsByIdAsync(
            workspaceSlug,
            repositorySlug,
            pullRequestId,
            cancellationToken);

        var mappedComments = commentsById.Values
            .Select(comment => MapComment(comment, commentsById))
            .ToDictionary(comment => comment.Id);
        var matchingRootIds = mappedComments.Values
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
            .Select(comment => comment.Id)
            .ToHashSet();

        return mappedComments.Values
            .Select(comment => new
            {
                Comment = comment,
                RootId = FindRootCommentId(commentsById[comment.Id], commentsById),
            })
            .Where(item => matchingRootIds.Contains(item.RootId) && !item.Comment.IsPending)
            .OrderBy(item => mappedComments[item.RootId].ToLine ?? mappedComments[item.RootId].FromLine ?? int.MaxValue)
            .ThenBy(item => mappedComments[item.RootId].CreatedOn ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Comment.CreatedOn ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Comment.Id)
            .Select(item => item.Comment)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<long, PullRequestCommentDto>> GetCachedCommentsByIdAsync(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(workspaceSlug, repositorySlug, pullRequestId);
        var now = _timeProvider.GetUtcNow();
        if (_commentsCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > now)
        {
            return cached.CommentsById;
        }

        var commentsById = await GetCommentsByIdAsync(
            BuildCommentsListEndpoint(workspaceSlug, repositorySlug, pullRequestId),
            cancellationToken);
        _commentsCache[cacheKey] = new CachedComments(commentsById, now + CommentsCacheDuration);
        return commentsById;
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
        var inline = ResolveInline(comment, commentsById);
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
            inline?.Path?.Trim(),
            inline?.From,
            inline?.To,
            string.IsNullOrWhiteSpace(comment.User?.Uuid) ? null : comment.User.Uuid.Trim());
    }

    private static void ValidateImageUri(
        string workspaceSlug,
        string repositorySlug,
        Uri imageUri)
    {
        if (!imageUri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "La imagen debe pertenecer al repositorio de Bitbucket actual.",
                nameof(imageUri));
        }

        var segments = imageUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (imageUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(imageUri.Host, "bitbucket.org", StringComparison.OrdinalIgnoreCase) ||
            segments.Length < 4 ||
            !string.Equals(segments[0], workspaceSlug, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], repositorySlug, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[2], "images", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "La imagen debe pertenecer al repositorio de Bitbucket actual.",
                nameof(imageUri));
        }
    }

    private static string? DetectSupportedImageContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 &&
            content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (content.Length >= 6 &&
            (content[..6].SequenceEqual("GIF87a"u8) || content[..6].SequenceEqual("GIF89a"u8)))
        {
            return "image/gif";
        }

        return content.Length >= 2 && content[0] == 'B' && content[1] == 'M'
            ? "image/bmp"
            : null;
    }

    private static long FindRootCommentId(
        PullRequestCommentDto comment,
        IReadOnlyDictionary<long, PullRequestCommentDto> commentsById)
    {
        var visitedCommentIds = new HashSet<long>();
        var currentComment = comment;
        while (currentComment.Parent?.Id is long parentId &&
               commentsById.TryGetValue(parentId, out var parentComment))
        {
            if (!visitedCommentIds.Add(parentId))
            {
                throw new BitbucketPullRequestCommentException(
                    "La respuesta de Bitbucket contiene una relación circular entre comentarios.");
            }

            currentComment = parentComment;
        }

        return currentComment.Id!.Value;
    }

    private static InlineDto? ResolveInline(
        PullRequestCommentDto comment,
        IReadOnlyDictionary<long, PullRequestCommentDto> commentsById)
    {
        var visitedCommentIds = new HashSet<long>();
        var currentComment = comment;
        while (true)
        {
            if (currentComment.Inline is not null)
            {
                return currentComment.Inline;
            }

            var parentId = currentComment.Parent?.Id;
            if (parentId is null || !commentsById.TryGetValue(parentId.Value, out currentComment))
            {
                return null;
            }

            if (!visitedCommentIds.Add(parentId.Value))
            {
                throw new BitbucketPullRequestCommentException(
                    "La respuesta de Bitbucket contiene una relación circular entre comentarios.");
            }
        }
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

    private static Uri BuildCommentEndpoint(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId) =>
        new($"{BuildCommentsEndpoint(workspaceSlug, repositorySlug, pullRequestId).AbsoluteUri}/{commentId.ToString(CultureInfo.InvariantCulture)}");

    private static Uri BuildResolveEndpoint(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId,
        long commentId) =>
        new($"{BuildCommentEndpoint(workspaceSlug, repositorySlug, pullRequestId, commentId).AbsoluteUri}/resolve");

    private static void ValidatePullRequestContext(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        if (pullRequestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pullRequestId));
        }
    }

    private static void ValidateCommentId(long commentId, string parameterName)
    {
        if (commentId <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static Uri BuildCommentsListEndpoint(
        string workspaceSlug,
        string repositorySlug,
        long pullRequestId) =>
        new($"{BuildCommentsEndpoint(workspaceSlug, repositorySlug, pullRequestId).AbsoluteUri}?pagelen=100");

    private static string BuildCacheKey(string workspaceSlug, string repositorySlug, long pullRequestId) =>
        $"{workspaceSlug.Trim().ToLowerInvariant()}/{repositorySlug.Trim().ToLowerInvariant()}/{pullRequestId.ToString(CultureInfo.InvariantCulture)}";

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

    private sealed record CreateReplyRequestDto(
        [property: JsonPropertyName("content")] ContentDto Content,
        [property: JsonPropertyName("parent")] ParentDto Parent);

    private sealed record CreateCommentRequestDto(
        [property: JsonPropertyName("content")] ContentDto Content,
        [property: JsonPropertyName("inline")] InlineDto? Inline);

    private sealed record UpdateCommentRequestDto(
        [property: JsonPropertyName("content")] ContentDto Content);

    private sealed record CreateReplyResponseDto(
        [property: JsonPropertyName("id")] long? Id);

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
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("uuid")] string? Uuid);

    private sealed class ResolutionDto
    {
    }

    private sealed record CachedComments(
        IReadOnlyDictionary<long, PullRequestCommentDto> CommentsById,
        DateTimeOffset ExpiresAt);
}
