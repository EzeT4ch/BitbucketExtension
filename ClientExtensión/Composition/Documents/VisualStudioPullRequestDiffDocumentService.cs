using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ClientExtension.Application.Abstractions;

namespace ClientExtension.Composition.Documents;

internal sealed class VisualStudioPullRequestDiffDocumentService : IPullRequestDiffDocumentService
{
    private const string RootSuffixEnvironmentVariable = "COMMENTEXTENSION_VS_ROOT_SUFFIX";
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    public async Task OpenAsync(
        PullRequestDiffDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paths = BuildDocumentPaths(document);
        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.Before)!);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.After)!);
            await File.WriteAllTextAsync(
                paths.Before,
                document.BeforeContent,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            await File.WriteAllTextAsync(
                paths.After,
                document.AfterContent,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
        finally
        {
            _writeSemaphore.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();
        OpenNativeComparison(paths, document);
    }

    private static void OpenNativeComparison(
        DiffDocumentPaths paths,
        PullRequestDiffDocument document)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDevenvPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var rootSuffix = Environment.GetEnvironmentVariable(RootSuffixEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(rootSuffix))
        {
            startInfo.ArgumentList.Add("/RootSuffix");
            startInfo.ArgumentList.Add(rootSuffix.Trim());
        }

        startInfo.ArgumentList.Add("/Diff");
        startInfo.ArgumentList.Add(paths.Before);
        startInfo.ArgumentList.Add(paths.After);
        startInfo.ArgumentList.Add(document.BeforeLabel);
        startInfo.ArgumentList.Add(document.AfterLabel);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Visual Studio no pudo iniciar el visor nativo de diferencias.");
    }

    private static DiffDocumentPaths BuildDocumentPaths(PullRequestDiffDocument document)
    {
        var rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CommentExtensionBitbucket",
            "comparisons");
        var contextDirectory = SanitizeFileName(
            $"{document.WorkspaceSlug}-{document.RepositorySlug}-PR-{document.PullRequestId}");
        var comparisonHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{document.BeforePath}\n{document.AfterPath}")))[..8];
        var comparisonDirectory = Path.Combine(rootDirectory, contextDirectory, comparisonHash);

        return new DiffDocumentPaths(
            Path.Combine(
                comparisonDirectory,
                "before",
                GetSafeSourceFileName(document.BeforePath)),
            Path.Combine(
                comparisonDirectory,
                "after",
                GetSafeSourceFileName(document.AfterPath)));
    }

    private static string GetSafeSourceFileName(string filePath)
    {
        var sourceFileName = Path.GetFileName(filePath.Replace('/', Path.DirectorySeparatorChar));
        return SanitizeFileName(string.IsNullOrWhiteSpace(sourceFileName) ? "file.txt" : sourceFileName);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "diff" : sanitized;
    }

    private static string ResolveDevenvPath()
    {
        var appDirectory = Environment.GetEnvironmentVariable("VSAPPIDDIR");
        var installationDirectory = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(appDirectory)
                ? null
                : Path.Combine(appDirectory, "devenv.exe"),
            string.IsNullOrWhiteSpace(installationDirectory)
                ? null
                : Path.Combine(installationDirectory, "Common7", "IDE", "devenv.exe"),
        };

        return candidates.FirstOrDefault(candidate =>
                   candidate is not null && File.Exists(candidate))
               ?? "devenv.exe";
    }

    private sealed record DiffDocumentPaths(string Before, string After);
}
