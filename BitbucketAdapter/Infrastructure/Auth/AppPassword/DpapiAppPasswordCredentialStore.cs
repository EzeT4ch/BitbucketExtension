using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using BitbucketAdapter.Application.Abstractions.AppPassword;
using BitbucketAdapter.Domain.Auth.AppPassword;

namespace BitbucketAdapter.Infrastructure.Auth.AppPassword;

[SupportedOSPlatform("windows")]
public sealed class DpapiAppPasswordCredentialStore : IAppPasswordCredentialStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommentExtensionBitbucket",
        "bitbucket-app-password.dat");

    private readonly string _filePath;

    public DpapiAppPasswordCredentialStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public async Task<BitbucketAppPasswordCredential?> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_filePath, cancellationToken);
            var jsonBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<BitbucketAppPasswordCredential>(jsonBytes);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(BitbucketAppPasswordCredential credential, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(credential);
        var encryptedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encryptedBytes, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}
