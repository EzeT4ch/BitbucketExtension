using System.Diagnostics;
using System.Text;
using ClientExtension.Application.Abstractions;

namespace ClientExtension.Composition.Documents;

internal sealed class VisualStudioPullRequestDiffDocumentService : IPullRequestDiffDocumentService
{
    public async Task OpenAsync(
        PullRequestDiffDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var comparisonDirectory = Path.Combine(
            Path.GetTempPath(),
            "CommentExtensionBitbucket",
            "Diffs",
            Guid.NewGuid().ToString("N"));
        var beforeFile = await WriteSnapshotAsync(
            comparisonDirectory,
            "Base",
            document.BeforePath,
            document.BeforeContent,
            cancellationToken);
        var afterFile = await WriteSnapshotAsync(
            comparisonDirectory,
            "PullRequest",
            document.AfterPath,
            document.AfterContent,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(FindVisualStudioExecutable())
        {
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("/Diff");
        startInfo.ArgumentList.Add(beforeFile);
        startInfo.ArgumentList.Add(afterFile);
        startInfo.ArgumentList.Add(document.BeforeLabel);
        startInfo.ArgumentList.Add(document.AfterLabel);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Visual Studio no pudo iniciar su comparador de archivos.");
        }
    }

    private static async Task<string> WriteSnapshotAsync(
        string comparisonDirectory,
        string side,
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var relativePath = BuildSafeRelativePath(filePath);
        var snapshotPath = Path.Combine(comparisonDirectory, side, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        await File.WriteAllTextAsync(
            snapshotPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        return snapshotPath;
    }

    private static string BuildSafeRelativePath(string filePath)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var segments = filePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment is not "." and not "..")
            .Select(segment => string.Concat(segment.Select(character =>
                invalidCharacters.Contains(character) ? '_' : character)))
            .Where(segment => segment.Length > 0)
            .ToArray();

        return segments.Length == 0 ? "archivo.txt" : Path.Combine(segments);
    }

    private static string FindVisualStudioExecutable()
    {
        var appDirectory = Environment.GetEnvironmentVariable("VSAPPIDDIR");
        if (!string.IsNullOrWhiteSpace(appDirectory))
        {
            var executable = Path.Combine(appDirectory, "devenv.exe");
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        var installDirectory = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(installDirectory))
        {
            var executable = Path.Combine(installDirectory, "Common7", "IDE", "devenv.exe");
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        throw new InvalidOperationException(
            "No se encontró devenv.exe para abrir el comparador nativo de Visual Studio.");
    }
}
