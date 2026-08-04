using ClientExtension.Presentation.Shared.Layout;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

internal sealed class PullRequestDiffControl : ThemedRemoteUserControl
{
    private const string DiffResources =
        "ClientExtension.Presentation.Bitbucket.PullRequests.PullRequestDiffResources.xaml";

    public PullRequestDiffControl(PullRequestDiffViewData dataContext)
        : base(dataContext)
    {
        ResourceDictionaries.AddEmbeddedResource(DiffResources);
    }
}
