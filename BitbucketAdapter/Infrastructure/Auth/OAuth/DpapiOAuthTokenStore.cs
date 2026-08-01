using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Domain.Auth.OAuth;

namespace BitbucketAdapter.Infrastructure.Auth.OAuth;

[SupportedOSPlatform("windows")]
public sealed class DpapiOAuthTokenStore : IOAuthTokenStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommentExtensionBitbucket",
        "bitbucket-oauth-token.dat");

    private readonly string _filePath;

    public DpapiOAuthTokenStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public async Task<OAuthToken?> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_filePath, cancellationToken);
            var jsonBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<OAuthToken>(jsonBytes);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(OAuthToken token, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(token);
        var encryptedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encryptedBytes, cancellationToken);
    }
}