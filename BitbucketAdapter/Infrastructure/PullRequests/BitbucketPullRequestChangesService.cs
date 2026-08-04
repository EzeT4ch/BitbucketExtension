using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Domain.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Infrastructure.PullRequests;

public sealed class BitbucketPullRequestChangesService : IBitbucketPullRequestChangesService
{
    private static readonly Uri ApiBaseEndpoint = new("https://api.bitbucket.org/");
    private static readonly IReadOnlyDictionary<string, string> JsonHeaders =
        new Dictionary<string, string> { ["Accept"] = "application/json" };
    private static readonly IReadOnlyDictionary<string, string> DiffHeaders =
        new Dictionary<string, string> { ["Accept"] = "text/plain" };
    private static readonly IReadOnlyDictionary<string, string> SourceHeaders =
        new Dictionary<string, string> { ["Accept"] = "*/*" };

    private readonly IHttpRequestSender _requestSender;

    public BitbucketPullRequestChangesService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<BitbucketPullRequestFileChange>> GetFilesAsync(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        CancellationToken cancellationToken)
    {
        ValidateContext(workspaceSlug, repositorySlug, sourceCommitHash, destinationCommitHash);

        var filesByPath = new Dictionary<string, BitbucketPullRequestFileChange>(StringComparer.Ordinal);
        var visitedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? currentEndpoint = BuildDiffStatEndpoint(
            workspaceSlug,
            repositorySlug,
            sourceCommitHash,
            destinationCommitHash);

        while (currentEndpoint is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedEndpoints.Add(currentEndpoint.AbsoluteUri))
            {
                throw new BitbucketPullRequestChangesException(
                    "La paginación de archivos modificados contiene un enlace repetido.");
            }

            var request = new RequestMessage(HttpMethodType.Get, currentEndpoint, Headers: JsonHeaders);
            var response = await _requestSender.SendAsync(request, cancellationToken);
            var page = ParsePage(response);

            foreach (var value in page.Values)
            {
                var before = ParseVersion(value.Old);
                var after = ParseVersion(value.New);
                var path = FirstNotEmpty(after?.Path, before?.Path);
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(value.Status))
                {
                    throw new BitbucketPullRequestChangesException(
                        "La respuesta de Bitbucket contiene una modificación de archivo inválida.");
                }

                var previousPath = value.Old?.Path?.Trim();
                var file = new BitbucketPullRequestFileChange(
                    path,
                    string.Equals(previousPath, path, StringComparison.Ordinal) ? null : previousPath,
                    value.Status.Trim(),
                    Math.Max(value.LinesAdded ?? 0, 0),
                    Math.Max(value.LinesRemoved ?? 0, 0),
                    before,
                    after);
                filesByPath.TryAdd(file.Path, file);
            }

            currentEndpoint = ParseNextEndpoint(page.Next);
        }

        return filesByPath.Values
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string> GetFileDiffAsync(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        string filePath,
        CancellationToken cancellationToken)
    {
        ValidateContext(workspaceSlug, repositorySlug, sourceCommitHash, destinationCommitHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var endpoint = BuildDiffEndpoint(
            workspaceSlug,
            repositorySlug,
            sourceCommitHash,
            destinationCommitHash,
            filePath);
        var request = new RequestMessage(HttpMethodType.Get, endpoint, Headers: DiffHeaders);
        var response = await _requestSender.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestChangesException(
                $"Bitbucket no pudo obtener el diff del archivo. Status: {response.StatusCode}.");
        }

        return response.Content ?? string.Empty;
    }

    public async Task<BitbucketPullRequestFileComparison> GetFileComparisonAsync(
        string workspaceSlug,
        string repositorySlug,
        BitbucketPullRequestFileChange fileChange,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        ArgumentNullException.ThrowIfNull(fileChange);

        if (fileChange.Before is null && fileChange.After is null)
        {
            throw new BitbucketPullRequestChangesException(
                "El archivo modificado no contiene versiones para comparar.");
        }

        var before = await GetFileSnapshotAsync(
            workspaceSlug,
            repositorySlug,
            fileChange.Before,
            cancellationToken);
        var after = await GetFileSnapshotAsync(
            workspaceSlug,
            repositorySlug,
            fileChange.After,
            cancellationToken);

        return new BitbucketPullRequestFileComparison(before, after);
    }

    private async Task<BitbucketPullRequestFileSnapshot?> GetFileSnapshotAsync(
        string workspaceSlug,
        string repositorySlug,
        BitbucketPullRequestFileVersion? version,
        CancellationToken cancellationToken)
    {
        if (version is null)
        {
            return null;
        }

        var endpoint = BuildSourceEndpoint(
            workspaceSlug,
            repositorySlug,
            version.Revision,
            version.Path);
        var request = new RequestMessage(HttpMethodType.Get, endpoint, Headers: SourceHeaders);
        var response = await _requestSender.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestChangesException(
                $"Bitbucket no pudo obtener {version.Path} en la revisión {version.Revision}. Status: {response.StatusCode}.");
        }

        return new BitbucketPullRequestFileSnapshot(
            version.Path,
            version.Revision,
            response.Content ?? string.Empty);
    }

    private static DiffStatPage ParsePage(ResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BitbucketPullRequestChangesException(
                $"Bitbucket no pudo obtener los archivos modificados. Status: {response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new BitbucketPullRequestChangesException(
                "La respuesta de archivos modificados de Bitbucket vino vacía.");
        }

        try
        {
            var page = JsonSerializer.Deserialize<DiffStatPageDto>(response.Content);
            if (page?.Values is null)
            {
                throw new BitbucketPullRequestChangesException(
                    "La respuesta de Bitbucket no contiene una colección de archivos modificados.");
            }

            return new DiffStatPage(page.Values, page.Next);
        }
        catch (JsonException exception)
        {
            throw new BitbucketPullRequestChangesException(
                "No se pudo interpretar la respuesta de archivos modificados de Bitbucket.",
                exception);
        }
    }

    private static Uri BuildDiffStatEndpoint(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash) =>
        new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Escape(workspaceSlug)}/{Escape(repositorySlug)}/diffstat/{BuildSpec(sourceCommitHash, destinationCommitHash)}?topic=true");

    private static Uri BuildDiffEndpoint(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash,
        string filePath) =>
        new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Escape(workspaceSlug)}/{Escape(repositorySlug)}/diff/{BuildSpec(sourceCommitHash, destinationCommitHash)}?topic=true&path={Uri.EscapeDataString(filePath.Trim())}");

    private static Uri BuildSourceEndpoint(
        string workspaceSlug,
        string repositorySlug,
        string revision,
        string filePath) =>
        new(
            ApiBaseEndpoint,
            $"2.0/repositories/{Escape(workspaceSlug)}/{Escape(repositorySlug)}/src/{Escape(revision)}/{EscapePath(filePath)}");

    private static BitbucketPullRequestFileVersion? ParseVersion(CommitFileDto? file)
    {
        if (file is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(file.Path) ||
            !Uri.TryCreate(file.Links?.Self?.Href, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(endpoint.Host, ApiBaseEndpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new BitbucketPullRequestChangesException(
                "La respuesta de Bitbucket contiene una versión de archivo inválida.");
        }

        var segments = endpoint.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 6 ||
            !string.Equals(segments[0], "2.0", StringComparison.Ordinal) ||
            !string.Equals(segments[1], "repositories", StringComparison.Ordinal) ||
            !string.Equals(segments[4], "src", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(segments[5]))
        {
            throw new BitbucketPullRequestChangesException(
                "La respuesta de Bitbucket no informa la revisión del archivo modificado.");
        }

        return new BitbucketPullRequestFileVersion(
            file.Path.Trim(),
            Uri.UnescapeDataString(segments[5]));
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
            throw new BitbucketPullRequestChangesException(
                "La respuesta de archivos modificados contiene un enlace de paginación inválido.");
        }

        return nextEndpoint;
    }

    private static void ValidateContext(
        string workspaceSlug,
        string repositorySlug,
        string sourceCommitHash,
        string destinationCommitHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommitHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationCommitHash);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());

    private static string EscapePath(string value) => string.Join(
        '/',
        value.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    private static string BuildSpec(string sourceCommitHash, string destinationCommitHash) =>
        $"{Escape(sourceCommitHash)}..{Escape(destinationCommitHash)}";

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record DiffStatPageDto(
        [property: JsonPropertyName("values")] List<DiffStatDto>? Values,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record DiffStatPage(
        IReadOnlyList<DiffStatDto> Values,
        string? Next);

    private sealed record DiffStatDto(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("lines_added")] int? LinesAdded,
        [property: JsonPropertyName("lines_removed")] int? LinesRemoved,
        [property: JsonPropertyName("old")] CommitFileDto? Old,
        [property: JsonPropertyName("new")] CommitFileDto? New);

    private sealed record CommitFileDto(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("links")] CommitFileLinksDto? Links);

    private sealed record CommitFileLinksDto(
        [property: JsonPropertyName("self")] LinkDto? Self);

    private sealed record LinkDto(
        [property: JsonPropertyName("href")] string? Href);
}
