namespace ClientExtension.Application.Abstractions;

internal interface IPullRequestDiffDocumentService
{
    Task OpenAsync(PullRequestDiffDocument document, CancellationToken cancellationToken);
}

internal sealed record PullRequestDiffDocument(
    string WorkspaceSlug,
    string RepositorySlug,
    long PullRequestId,
    string BeforePath,
    string BeforeContent,
    string BeforeLabel,
    string AfterPath,
    string AfterContent,
    string AfterLabel);
