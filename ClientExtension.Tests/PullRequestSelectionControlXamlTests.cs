using System.Xml.Linq;
using Xunit;

namespace ClientExtension.Tests;

public sealed class PullRequestSelectionControlXamlTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void DiffCommentCard_IsConstrainedToTheScrollViewport()
    {
        var document = XDocument.Load(GetControlPath());
        var viewport = Assert.Single(document.Descendants(Presentation + "Grid"), element =>
            (string?)element.Attribute(Xaml + "Name") == "DiffCommentViewport");

        var widthBinding = (string?)viewport.Attribute("Width");
        Assert.Contains("AncestorType={x:Type ScrollContentPresenter}", widthBinding);
        Assert.Contains("Path=ActualWidth", widthBinding);
    }

    private static string GetControlPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "ClientExtensión",
            "Presentation",
            "Bitbucket",
            "PullRequests",
            "PullRequestSelectionControl.xaml"));
}
