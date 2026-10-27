using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BitbucketAdapter.Domain.PullRequests;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

internal static class PullRequestCommentImageCache
{
    private static readonly SemaphoreSlim WriteSemaphore = new(1, 1);
    private static readonly ConcurrentDictionary<string, string> LocalUrlsBySource = new(StringComparer.Ordinal);
    private static readonly string CacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "CommentExtensionBitbucket",
        "comment-images",
        Environment.ProcessId.ToString());

    static PullRequestCommentImageCache()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteCacheDirectory();
    }

    internal static bool TryGetLocalUrl(Uri sourceUri, out string localUrl)
    {
        if (LocalUrlsBySource.TryGetValue(sourceUri.AbsoluteUri, out var cachedUrl) &&
            File.Exists(new Uri(cachedUrl).LocalPath))
        {
            localUrl = cachedUrl;
            return true;
        }

        localUrl = string.Empty;
        return false;
    }

    internal static async Task<string> StoreAsync(
        Uri sourceUri,
        BitbucketPullRequestCommentImage image,
        CancellationToken cancellationToken)
    {
        var fileName = $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUri.AbsoluteUri)))}" +
                       GetExtension(image.ContentType);
        var filePath = Path.Combine(CacheDirectory, fileName);

        await WriteSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            if (!File.Exists(filePath))
            {
                await File.WriteAllBytesAsync(filePath, image.Content, cancellationToken);
            }
        }
        finally
        {
            WriteSemaphore.Release();
        }

        var localUrl = new Uri(filePath).AbsoluteUri;
        LocalUrlsBySource[sourceUri.AbsoluteUri] = localUrl;
        return localUrl;
    }

    private static string GetExtension(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        _ => throw new NotSupportedException($"Image content type '{contentType}' is not supported."),
    };

    private static void DeleteCacheDirectory()
    {
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
