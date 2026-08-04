using ClientExtension.Presentation.Shared.Layout;

namespace ClientExtension.Presentation.Bitbucket.PullRequests.Diff;

internal sealed class PullRequestDiffControl : ThemedRemoteUserControl
{
    private const string DiffResources =
        "ClientExtension.Presentation.Bitbucket.PullRequests.Diff.PullRequestDiffResources.xaml";

    public PullRequestDiffControl(PullRequestDiffViewData dataContext)
        : base(dataContext)
    {
        ResourceDictionaries.AddEmbeddedResource(DiffResources);
    }
}
