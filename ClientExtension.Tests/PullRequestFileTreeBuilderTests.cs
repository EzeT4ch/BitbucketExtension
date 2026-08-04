using BitbucketAdapter.Domain.PullRequests;
using ClientExtension.Presentation.Bitbucket.PullRequests.Data;
using Xunit;

namespace ClientExtension.Tests;

public sealed class PullRequestFileTreeBuilderTests
{
    [Fact]
    public void Build_WhenFilesBelongToDifferentProjects_ReturnsCompleteHierarchy()
    {
        var files = new[]
        {
            CreateFile("Admin.ApiService/Program.cs"),
            CreateFile("Admin.Web/src/app/app.component.ts"),
            CreateFile("Admin.Web/src/app/services/inventory.service.ts"),
            CreateFile("Admin.Web\\src\\assets\\settings.json"),
        };

        var roots = PullRequestFileTreeBuilder.Build(files, _ => { });

        Assert.Collection(
            roots,
            api =>
            {
                Assert.Equal("Admin.ApiService", api.Name);
                Assert.Equal("1 archivo", api.Details);
                Assert.Single(api.Children);
            },
            web =>
            {
                Assert.Equal("Admin.Web", web.Name);
                Assert.Equal("3 archivos", web.Details);
                var src = Assert.Single(web.Children);
                Assert.Equal("src", src.Name);
                Assert.Equal("3 archivos", src.Details);
                Assert.Equal(2, src.Children.Count);
                Assert.Contains(src.Children, node => node.Name == "app" && node.Children.Count == 2);
                Assert.Contains(src.Children, node => node.Name == "assets" && node.Children.Count == 1);
            });
    }

    private static BitbucketPullRequestFileChange CreateFile(string path) =>
        new(
            path,
            null,
            "modified",
            1,
            1,
            new BitbucketPullRequestFileVersion(path, "before"),
            new BitbucketPullRequestFileVersion(path, "after"));
}
