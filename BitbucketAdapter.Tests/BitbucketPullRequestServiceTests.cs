using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketPullRequestServiceTests
{
    [Fact]
    public async Task GetOpenAsync_FollowsPaginationAndReturnsNewestUniquePullRequestsFirst()
    {
        var sender = new QueueRequestSender(
            Success("""
                {
                  "values": [
                    {
                      "id": 3,
                      "title": "Newest",
                      "state": "OPEN",
                      "author": { "display_name": "Zoe" },
                      "source": { "branch": { "name": "feature/newest" } },
                      "destination": { "branch": { "name": "develop" } },
                      "updated_on": "2026-08-03T12:00:00Z"
                    },
                    {
                      "id": 1,
                      "title": "Oldest",
                      "state": "OPEN",
                      "author": { "display_name": "Ana" },
                      "source": { "branch": { "name": "feature/oldest" } },
                      "destination": { "branch": { "name": "develop" } },
                      "updated_on": "2026-08-01T12:00:00Z"
                    }
                  ],
                  "next": "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests?page=2&state=OPEN"
                }
                """),
            Success("""
                {
                  "values": [
                    {
                      "id": 1,
                      "title": "Duplicate",
                      "state": "OPEN",
                      "source": { "branch": { "name": "duplicate" } },
                      "destination": { "branch": { "name": "develop" } }
                    },
                    {
                      "id": 2,
                      "title": "Middle",
                      "state": "OPEN",
                      "author": { "display_name": "Bob" },
                      "source": { "branch": { "name": "feature/middle" } },
                      "destination": { "branch": { "name": "develop" } },
                      "updated_on": "2026-08-02T12:00:00Z"
                    }
                  ]
                }
                """));
        var service = new BitbucketPullRequestService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetOpenAsync("team", "repo", cancellationTokenSource.Token);

        Assert.Equal([3L, 2L, 1L], result.Select(pullRequest => pullRequest.Id));
        Assert.Equal(2, sender.Requests.Count);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests?state=OPEN",
            sender.Requests[0].Request.Endpoint.AbsoluteUri);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests?page=2&state=OPEN",
            sender.Requests[1].Request.Endpoint.AbsoluteUri);
        Assert.Equal("application/json", sender.Requests[0].Request.Headers?["Accept"]);
        Assert.All(sender.Requests, request => Assert.Equal(cancellationTokenSource.Token, request.CancellationToken));
    }

    [Fact]
    public async Task GetOpenAsync_EscapesWorkspaceAndRepositoryAndUsesDecoratedCredential()
    {
        var rawSender = new QueueRequestSender(Success("{\"values\":[]}"));
        var authenticatedSender = new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token")));
        var service = new BitbucketPullRequestService(authenticatedSender);

        await service.GetOpenAsync("team space", "repo name", CancellationToken.None);

        var request = rawSender.Requests.Single().Request;
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/pullrequests?state=OPEN",
            request.Endpoint.AbsoluteUri);
        var credential = Assert.IsType<BearerTokenCredential>(request.Credential);
        Assert.Equal("access-token", credential.Token);
    }

    [Fact]
    public async Task GetByIdAsync_MapsDetailedPullRequestAndPropagatesRequestData()
    {
        var sender = new QueueRequestSender(Success("""
            {
              "id": 42,
              "title": "Add Pull Request browser",
              "description": "Full description",
              "reason": "Ready for review",
              "state": "OPEN",
              "author": { "display_name": "Eze" },
              "source": {
                "branch": { "name": "feature/pr-browser" },
                "commit": { "hash": "abc123" }
              },
              "destination": {
                "branch": { "name": "develop" },
                "commit": { "hash": "def456" }
              },
              "created_on": "2026-08-01T10:00:00Z",
              "updated_on": "2026-08-03T12:00:00Z",
              "comment_count": 5,
              "task_count": 2,
              "draft": true,
              "close_source_branch": true,
              "reviewers": [
                { "display_name": "Zoe" },
                { "display_name": "Ana" },
                { "display_name": "ana" }
              ],
              "links": { "html": { "href": "https://bitbucket.org/team/repo/pull-requests/42" } }
            }
            """));
        var service = new BitbucketPullRequestService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetByIdAsync("team", "repo", 42, cancellationTokenSource.Token);

        Assert.Equal(42, result.Summary.Id);
        Assert.Equal("Add Pull Request browser", result.Summary.Title);
        Assert.Equal("Eze", result.Summary.AuthorDisplayName);
        Assert.Equal("feature/pr-browser", result.Summary.SourceBranch);
        Assert.Equal("develop", result.Summary.DestinationBranch);
        Assert.Equal(5, result.Summary.CommentCount);
        Assert.Equal(2, result.Summary.TaskCount);
        Assert.True(result.Summary.IsDraft);
        Assert.Equal("Full description", result.Description);
        Assert.Equal(["Ana", "Zoe"], result.Reviewers);
        Assert.Equal("abc123", result.SourceCommitHash);
        Assert.Equal("def456", result.DestinationCommitHash);
        Assert.True(result.CloseSourceBranch);
        Assert.Equal("https://bitbucket.org/team/repo/pull-requests/42", result.HtmlUrl);
        var request = sender.Requests.Single();
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42",
            request.Request.Endpoint.AbsoluteUri);
        Assert.Equal("application/json", request.Request.Headers?["Accept"]);
        Assert.Equal(cancellationTokenSource.Token, request.CancellationToken);
    }

    [Fact]
    public async Task GetOpenAsync_WhenNoPullRequestsAreAvailable_ReturnsEmptyCollection()
    {
        var service = new BitbucketPullRequestService(
            new QueueRequestSender(Success("{\"values\":[]}")));

        var result = await service.GetOpenAsync("team", "repo", CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(500, false, "{\"values\":[]}")]
    [InlineData(200, true, null)]
    [InlineData(200, true, "")]
    [InlineData(200, true, "not-json")]
    [InlineData(200, true, "{}")]
    [InlineData(200, true, "{\"values\":[{\"id\":1,\"title\":\"Missing branches\",\"state\":\"OPEN\"}]}")]
    public async Task GetOpenAsync_WhenResponseIsInvalid_ThrowsPullRequestException(
        int statusCode,
        bool isSuccessStatusCode,
        string? content)
    {
        var service = new BitbucketPullRequestService(
            new QueueRequestSender(new ResponseMessage(
                statusCode,
                isSuccessStatusCode,
                new Dictionary<string, string>(),
                content)));

        await Assert.ThrowsAsync<BitbucketPullRequestException>(
            () => service.GetOpenAsync("team", "repo", CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_WhenResponseIsInvalid_ThrowsPullRequestException()
    {
        var service = new BitbucketPullRequestService(
            new QueueRequestSender(Success("{}")));

        await Assert.ThrowsAsync<BitbucketPullRequestException>(
            () => service.GetByIdAsync("team", "repo", 42, CancellationToken.None));
    }

    [Fact]
    public async Task GetOpenAsync_WhenNextLinkTargetsAnotherHost_ThrowsPullRequestException()
    {
        var service = new BitbucketPullRequestService(
            new QueueRequestSender(Success("""
                {
                  "values": [],
                  "next": "https://example.com/2.0/repositories/team/repo/pullrequests?page=2"
                }
                """)));

        await Assert.ThrowsAsync<BitbucketPullRequestException>(
            () => service.GetOpenAsync("team", "repo", CancellationToken.None));
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
