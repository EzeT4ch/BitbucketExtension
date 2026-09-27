using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketPullRequestChangesServiceTests
{
    [Fact]
    public async Task GetFilesAsync_FollowsPaginationAndReturnsSortedUniqueFiles()
    {
        var sender = new QueueRequestSender(
            Success("""
                {
                  "values": [
                    {
                      "status": "modified",
                      "lines_added": 4,
                      "lines_removed": 2,
                      "old": {
                        "path": "src/Zeta.cs",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/base111/src/Zeta.cs" } }
                      },
                      "new": {
                        "path": "src/Zeta.cs",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/source123/src/Zeta.cs" } }
                      }
                    },
                    {
                      "status": "added",
                      "lines_added": 10,
                      "lines_removed": 0,
                      "old": null,
                      "new": {
                        "path": "README.md",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/source123/README.md" } }
                      }
                    }
                  ],
                  "next": "https://api.bitbucket.org/2.0/repositories/team/repo/diffstat/source123..dest456?page=2&topic=true"
                }
                """),
            Success("""
                {
                  "values": [
                    {
                      "status": "renamed",
                      "lines_added": 1,
                      "lines_removed": 1,
                      "old": {
                        "path": "src/Old.cs",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/base111/src/Old.cs" } }
                      },
                      "new": {
                        "path": "src/New.cs",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/source123/src/New.cs" } }
                      }
                    },
                    {
                      "status": "modified",
                      "old": {
                        "path": "src/Zeta.cs",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/base111/src/Zeta.cs" } }
                      },
                      "new": {
                        "path": "src/Zeta.cs",
                        "links": { "self": { "href": "https://api.bitbucket.org/2.0/repositories/team/repo/src/source123/src/Zeta.cs" } }
                      }
                    }
                  ]
                }
                """));
        var service = new BitbucketPullRequestChangesService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetFilesAsync(
            "team",
            "repo",
            "source123",
            "dest456",
            cancellationTokenSource.Token);

        Assert.Equal(["README.md", "src/New.cs", "src/Zeta.cs"], result.Select(file => file.Path));
        Assert.Equal("src/Old.cs", result[1].PreviousPath);
        Assert.Equal("base111", result[1].Before?.Revision);
        Assert.Equal("source123", result[1].After?.Revision);
        Assert.Equal(1, result[1].LinesAdded);
        Assert.Equal(1, result[1].LinesRemoved);
        Assert.Equal(2, sender.Requests.Count);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/diffstat/source123..dest456?topic=true&pagelen=500",
            sender.Requests[0].Request.Endpoint.AbsoluteUri);
        Assert.Equal("application/json", sender.Requests[0].Request.Headers?["Accept"]);
        Assert.All(sender.Requests, request => Assert.Equal(cancellationTokenSource.Token, request.CancellationToken));
    }

    [Fact]
    public async Task GetFileComparisonAsync_LoadsBothVersionsWithOriginalPaths()
    {
        var sender = new QueueRequestSender(
            Success("before content"),
            Success("after content"));
        var service = new BitbucketPullRequestChangesService(sender);
        var fileChange = new BitbucketAdapter.Domain.PullRequests.BitbucketPullRequestFileChange(
            "src/New File.cs",
            "src/Old File.cs",
            "renamed",
            1,
            1,
            new BitbucketAdapter.Domain.PullRequests.BitbucketPullRequestFileVersion(
                "src/Old File.cs",
                "base111"),
            new BitbucketAdapter.Domain.PullRequests.BitbucketPullRequestFileVersion(
                "src/New File.cs",
                "source123"));
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetFileComparisonAsync(
            "team space",
            "repo name",
            fileChange,
            cancellationTokenSource.Token);

        Assert.Equal("before content", result.Before?.Content);
        Assert.Equal("after content", result.After?.Content);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/src/base111/src/Old%20File.cs",
            sender.Requests[0].Request.Endpoint.AbsoluteUri);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/src/source123/src/New%20File.cs",
            sender.Requests[1].Request.Endpoint.AbsoluteUri);
        Assert.All(sender.Requests, request => Assert.Equal("*/*", request.Request.Headers?["Accept"]));
        Assert.All(sender.Requests, request => Assert.Equal(cancellationTokenSource.Token, request.CancellationToken));
    }

    [Fact]
    public async Task GetFileDiffAsync_UsesSourceDestinationOrderAndFiltersByEscapedPath()
    {
        const string expectedDiff = """
            diff --git a/src/File Name.cs b/src/File Name.cs
            --- a/src/File Name.cs
            +++ b/src/File Name.cs
            """;
        var sender = new QueueRequestSender(Success(expectedDiff));
        var service = new BitbucketPullRequestChangesService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetFileDiffAsync(
            "team space",
            "repo name",
            "source123",
            "dest456",
            "src/File Name.cs",
            cancellationTokenSource.Token);

        Assert.Equal(expectedDiff, result);
        var request = sender.Requests.Single();
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/diff/source123..dest456?topic=true&path=src%2FFile%20Name.cs",
            request.Request.Endpoint.AbsoluteUri);
        Assert.Equal("text/plain", request.Request.Headers?["Accept"]);
        Assert.Equal(cancellationTokenSource.Token, request.CancellationToken);
    }

    [Fact]
    public async Task GetFilesAsync_UsesCredentialAddedByAuthenticatedSender()
    {
        var rawSender = new QueueRequestSender(Success("{\"values\":[]}"));
        var authenticatedSender = new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token")));
        var service = new BitbucketPullRequestChangesService(authenticatedSender);

        await service.GetFilesAsync(
            "team",
            "repo",
            "source123",
            "dest456",
            CancellationToken.None);

        var credential = Assert.IsType<BearerTokenCredential>(rawSender.Requests.Single().Request.Credential);
        Assert.Equal("access-token", credential.Token);
    }

    [Fact]
    public async Task GetFilesAsync_WhenNoFilesAreAvailable_ReturnsEmptyCollection()
    {
        var service = new BitbucketPullRequestChangesService(
            new QueueRequestSender(Success("{\"values\":[]}")));

        var result = await service.GetFilesAsync(
            "team",
            "repo",
            "source123",
            "dest456",
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(500, false, "{\"values\":[]}")]
    [InlineData(200, true, null)]
    [InlineData(200, true, "")]
    [InlineData(200, true, "not-json")]
    [InlineData(200, true, "{}")]
    [InlineData(200, true, "{\"values\":[{\"status\":\"modified\"}]}")]
    public async Task GetFilesAsync_WhenResponseIsInvalid_ThrowsChangesException(
        int statusCode,
        bool isSuccessStatusCode,
        string? content)
    {
        var service = new BitbucketPullRequestChangesService(
            new QueueRequestSender(new ResponseMessage(
                statusCode,
                isSuccessStatusCode,
                new Dictionary<string, string>(),
                content)));

        await Assert.ThrowsAsync<BitbucketPullRequestChangesException>(() => service.GetFilesAsync(
            "team",
            "repo",
            "source123",
            "dest456",
            CancellationToken.None));
    }

    [Fact]
    public async Task GetFilesAsync_WhenNextLinkTargetsAnotherHost_ThrowsChangesException()
    {
        var service = new BitbucketPullRequestChangesService(
            new QueueRequestSender(Success("""
                {
                  "values": [],
                  "next": "https://example.com/2.0/repositories/team/repo/diffstat/source..dest?page=2"
                }
                """)));

        await Assert.ThrowsAsync<BitbucketPullRequestChangesException>(() => service.GetFilesAsync(
            "team",
            "repo",
            "source123",
            "dest456",
            CancellationToken.None));
    }

    [Fact]
    public async Task GetFileDiffAsync_WhenResponseFails_ThrowsChangesException()
    {
        var service = new BitbucketPullRequestChangesService(
            new QueueRequestSender(new ResponseMessage(
                404,
                false,
                new Dictionary<string, string>(),
                null)));

        await Assert.ThrowsAsync<BitbucketPullRequestChangesException>(() => service.GetFileDiffAsync(
            "team",
            "repo",
            "source123",
            "dest456",
            "src/File.cs",
            CancellationToken.None));
    }

    private static ResponseMessage Success(string content) =>
        new(200, true, new Dictionary<string, string>(), content);

    private sealed class QueueRequestSender : IHttpRequestSender
    {
        private readonly Queue<ResponseMessage> _responses;

        public QueueRequestSender(params ResponseMessage[] responses)
        {
            _responses = new Queue<ResponseMessage>(responses);
        }

        public List<SentRequest> Requests { get; } = [];

        public Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request, cancellationToken));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class StubAuthenticationProvider : IAuthenticationProvider
    {
        private readonly AuthCredential _credential;

        public StubAuthenticationProvider(AuthCredential credential)
        {
            _credential = credential;
        }

        public Task<AuthCredential> GetCredentialAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_credential);
    }

    private sealed record SentRequest(RequestMessage Request, CancellationToken CancellationToken);
}
