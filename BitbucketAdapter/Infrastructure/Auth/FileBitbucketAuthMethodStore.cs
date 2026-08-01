using System.Text.Json;
using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Domain.Auth;

namespace BitbucketAdapter.Infrastructure.Auth;

public sealed class FileBitbucketAuthMethodStore : IBitbucketAuthMethodStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommentExtensionBitbucket",
        "bitbucket-auth-method.json");

    private readonly string _filePath;

    public FileBitbucketAuthMethodStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public async Task<BitbucketAuthMethod?> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            return JsonSerializer.Deserialize<BitbucketAuthMethod>(json);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(BitbucketAuthMethod method, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(method), cancellationToken);
    }
}
