using System.Security.Cryptography;
using System.Text;
using BitbucketAdapter.Domain.PullRequests;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

internal static class PullRequestCommentImageCache
{
    private static readonly SemaphoreSlim WriteSemaphore = new(1, 1);
    private static readonly string CacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "CommentExtensionBitbucket",
        "comment-images",
        Environment.ProcessId.ToString());

    static PullRequestCommentImageCache()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteCacheDirectory();
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

        return new Uri(filePath).AbsoluteUri;
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
