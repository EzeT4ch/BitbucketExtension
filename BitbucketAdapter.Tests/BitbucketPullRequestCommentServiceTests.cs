using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketPullRequestCommentServiceTests
{
    [Fact]
    public async Task GetAllAsync_FollowsPaginationAndMapsCommentsInChronologicalOrder()
    {
        var sender = new QueueRequestSender(
            Success("""
                {
                  "values": [
                    {
                      "id": 2,
                      "content": { "raw": "Resolved thread" },
                      "user": { "display_name": "Zoe" },
                      "created_on": "2026-08-02T12:00:00Z",
                      "resolution": { "type": "pullrequest_comment_resolution" }
                    },
                    {
                      "id": 1,
                      "content": { "raw": "Please rename this" },
                      "user": { "display_name": "Ana" },
                      "created_on": "2026-08-01T12:00:00Z",
                      "inline": { "path": "src/Service.cs", "to": 42 }
                    }
                  ],
                  "next": "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments?page=2"
                }
                """),
            Success("""
                {
                  "values": [
                    {
                      "id": 3,
                      "parent": { "id": 2 },
                      "content": { "raw": "Done" },
                      "user": { "display_name": "Bob" },
                      "created_on": "2026-08-03T12:00:00Z",
                      "pending": true
                    },
                    {
                      "id": 1,
                      "content": { "raw": "Duplicate" },
                      "created_on": "2026-08-04T12:00:00Z"
                    }
                  ]
                }
                """));
        var service = new BitbucketPullRequestCommentService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetAllAsync(
            "team",
            "repo",
            42,
            cancellationTokenSource.Token);

        Assert.Equal([1L, 2L, 3L], result.Select(comment => comment.Id));
        var inlineComment = result[0];
        Assert.Equal("Please rename this", inlineComment.Content);
        Assert.Equal("Ana", inlineComment.AuthorDisplayName);
        Assert.Equal("src/Service.cs", inlineComment.FilePath);
        Assert.Equal(42, inlineComment.ToLine);
        Assert.False(inlineComment.IsResolved);

        var reply = result[2];
        Assert.Equal(2, reply.ParentId);
        Assert.True(reply.IsPending);
        Assert.True(reply.IsResolved);

        Assert.Equal(2, sender.Requests.Count);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments",
            sender.Requests[0].Request.Endpoint.AbsoluteUri);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments?page=2",
            sender.Requests[1].Request.Endpoint.AbsoluteUri);
        Assert.Equal("application/json", sender.Requests[0].Request.Headers?["Accept"]);
        Assert.All(sender.Requests, request => Assert.Equal(cancellationTokenSource.Token, request.CancellationToken));
    }

    [Fact]
    public async Task GetAllAsync_EscapesRepositoryContextAndUsesDecoratedCredential()
    {
        var rawSender = new QueueRequestSender(Success("{\"values\":[]}"));
        var authenticatedSender = new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token")));
        var service = new BitbucketPullRequestCommentService(authenticatedSender);

        await service.GetAllAsync("team space", "repo name", 7, CancellationToken.None);

        var request = rawSender.Requests.Single().Request;
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/pullrequests/7/comments",
            request.Endpoint.AbsoluteUri);
        var credential = Assert.IsType<BearerTokenCredential>(request.Credential);
        Assert.Equal("access-token", credential.Token);
    }

    [Fact]
    public async Task GetUnresolvedForFileAsync_ReturnsMatchingRootComments()
    {
        var sender = new QueueRequestSender(
            Success("""
                {
                  "values": [
                    {
                      "id": 12,
                      "content": { "raw": "Destination comment" },
                      "user": { "display_name": "Hernan" },
                      "created_on": "2026-08-01T12:00:00Z",
                      "inline": { "path": "src/UseCase.cs", "to": 212 }
                    },
                    {
                      "id": 13,
                      "content": { "raw": "Resolved" },
                      "user": { "display_name": "Ana" },
                      "created_on": "2026-08-01T13:00:00Z",
                      "resolution": {},
                      "inline": { "path": "src/UseCase.cs", "to": 220 }
                    },
                    {
                      "id": 14,
                      "parent": { "id": 12 },
                      "content": { "raw": "Reply" },
                      "user": { "display_name": "Ana" },
                      "created_on": "2026-08-01T14:00:00Z",
                      "inline": { "path": "src/UseCase.cs", "to": 212 }
                    },
                    {
                      "id": 15,
                      "content": { "raw": "Other file" },
                      "user": { "display_name": "Ana" },
                      "created_on": "2026-08-01T15:00:00Z",
                      "inline": { "path": "src/Other.cs", "to": 5 }
                    }
                  ],
                  "next": "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments?page=2"
                }
                """),
            Success("""
                {
                  "values": [
                    {
                      "id": 11,
                      "content": { "raw": "Base comment" },
                      "user": { "display_name": "Zoe" },
                      "created_on": "2026-07-31T12:00:00Z",
                      "inline": { "path": "src/UseCase.cs", "from": 10 }
                    }
                  ]
                }
                """));
        var service = new BitbucketPullRequestCommentService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetUnresolvedForFileAsync(
            "team",
            "repo",
            42,
            "src\\UseCase.cs",
            cancellationTokenSource.Token);

        Assert.Equal([11L, 12L], result.Select(comment => comment.Id));
        Assert.Equal(10, result[0].FromLine);
        Assert.Equal(212, result[1].ToLine);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments",
            sender.Requests[0].Request.Endpoint.AbsoluteUri);
        Assert.All(sender.Requests, request =>
            Assert.Equal(cancellationTokenSource.Token, request.CancellationToken));
    }

    [Fact]
    public async Task GetAllAsync_MapsDeletedCommentWithoutContent()
    {
        var service = new BitbucketPullRequestCommentService(
            new QueueRequestSender(Success("""
                {
                  "values": [
                    {
                      "id": 5,
                      "deleted": true,
                      "created_on": "2026-08-01T12:00:00Z"
                    }
                  ]
                }
                """)));

        var result = await service.GetAllAsync("team", "repo", 42, CancellationToken.None);

        var comment = Assert.Single(result);
        Assert.True(comment.IsDeleted);
        Assert.Equal("Comentario eliminado.", comment.Content);
        Assert.Equal("Autor desconocido", comment.AuthorDisplayName);
    }

    [Theory]
    [InlineData(500, false, "{\"values\":[]}")]
    [InlineData(200, true, null)]
    [InlineData(200, true, "")]
    [InlineData(200, true, "not-json")]
    [InlineData(200, true, "{}")]
    [InlineData(200, true, "{\"values\":[{\"id\":0}]}")]
    public async Task GetAllAsync_WhenResponseIsInvalid_ThrowsCommentException(
        int statusCode,
        bool isSuccessStatusCode,
        string? content)
    {
        var service = new BitbucketPullRequestCommentService(
            new QueueRequestSender(new ResponseMessage(
                statusCode,
                isSuccessStatusCode,
                new Dictionary<string, string>(),
                content)));

        await Assert.ThrowsAsync<BitbucketPullRequestCommentException>(() => service.GetAllAsync(
            "team",
            "repo",
            42,
            CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAsync_WhenNextLinkTargetsAnotherHost_ThrowsCommentException()
    {
        var service = new BitbucketPullRequestCommentService(
            new QueueRequestSender(Success("""
                {
                  "values": [],
                  "next": "https://example.com/2.0/repositories/team/repo/pullrequests/42/comments?page=2"
                }
                """)));

        await Assert.ThrowsAsync<BitbucketPullRequestCommentException>(() => service.GetAllAsync(
            "team",
            "repo",
            42,
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
