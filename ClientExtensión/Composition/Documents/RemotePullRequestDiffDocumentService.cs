using ClientExtension.Application.Abstractions;
using ClientExtension.Presentation.Bitbucket.PullRequests;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace ClientExtension.Composition.Documents;

internal sealed class RemotePullRequestDiffDocumentService : IPullRequestDiffDocumentService
{
    private readonly VisualStudioExtensibility _extensibility;
    private readonly PullRequestDiffViewData _dataContext;

    public RemotePullRequestDiffDocumentService(
        VisualStudioExtensibility extensibility,
        PullRequestDiffViewData dataContext)
    {
        _extensibility = extensibility;
        _dataContext = dataContext;
    }

    public async Task OpenAsync(
        PullRequestDiffDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        _dataContext.Load(document);
        await _extensibility.Shell().ShowToolWindowAsync<PullRequestDiffToolWindow>(
            activate: true,
            cancellationToken);
    }
}
