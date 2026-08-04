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
    string BeforeLabel,
    string BeforeContent,
    string AfterPath,
    string AfterLabel,
    string AfterContent,
    string RawDiff,
    IReadOnlyList<PullRequestDiffComment> Comments);

internal sealed record PullRequestDiffComment(
    long Id,
    int? BeforeLine,
    int? AfterLine,
    string AuthorDisplayName,
    string Content,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? UpdatedOn);
