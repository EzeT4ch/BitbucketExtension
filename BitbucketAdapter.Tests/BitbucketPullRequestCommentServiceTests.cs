using System.Text.Json;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.PullRequests;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;
using RequestAdapter.Infrastructure.Http;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketPullRequestCommentServiceTests
{
    [Fact]
    public async Task GetImageAsync_DownloadsRepositoryImageWithRegisteredCredential()
    {
        byte[] imageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var rawSender = new QueueRequestSender(new ResponseMessage(
            200,
            true,
            new Dictionary<string, string> { ["Content-Type"] = "image/png" },
            null,
            imageBytes));
        var service = new BitbucketPullRequestCommentService(new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token"))));
        using var cancellationTokenSource = new CancellationTokenSource();

        var image = await service.GetImageAsync(
            "team",
            "repo",
            new Uri("https://bitbucket.org/team/repo/images/123-image.png"),
            cancellationTokenSource.Token);

        Assert.Equal(imageBytes, image.Content);
        Assert.Equal("image/png", image.ContentType);
        var sent = Assert.Single(rawSender.Requests);
        Assert.Equal(HttpMethodType.Get, sent.Request.Method);
        Assert.Equal("https://bitbucket.org/team/repo/images/123-image.png", sent.Request.Endpoint.AbsoluteUri);
        Assert.Equal("image/*", sent.Request.Headers?["Accept"]);
        Assert.Equal(ResponseBodyMode.Binary, sent.Request.ResponseBodyMode);
        Assert.Equal(cancellationTokenSource.Token, sent.CancellationToken);
        Assert.Equal("access-token", Assert.IsType<BearerTokenCredential>(sent.Request.Credential).Token);
    }

    [Fact]
    public async Task GetImageAsync_WithHttpTransport_PreservesBinaryContent()
    {
        byte[] imageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imageBytes),
            };
            response.Content.Headers.ContentType = new("image/png");
            return response;
        }));
        var service = new BitbucketPullRequestCommentService(new HttpClientRequestSender(httpClient));

        var image = await service.GetImageAsync(
            "team",
            "repo",
            new Uri("https://bitbucket.org/team/repo/images/123-image.png"),
            CancellationToken.None);

        Assert.Equal(imageBytes, image.Content);
        Assert.Equal("image/png", image.ContentType);
    }

    [Theory]
    [InlineData("https://example.com/team/repo/images/image.png")]
    [InlineData("https://bitbucket.org/other/repo/images/image.png")]
    [InlineData("https://bitbucket.org/team/other/images/image.png")]
    [InlineData("http://bitbucket.org/team/repo/images/image.png")]
    public async Task GetImageAsync_WhenUrlIsOutsideCurrentRepository_RejectsBeforeSending(string imageUrl)
    {
        var sender = new QueueRequestSender();
        var service = new BitbucketPullRequestCommentService(sender);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetImageAsync(
            "team",
            "repo",
            new Uri(imageUrl),
            CancellationToken.None));

        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task CreateReplyAsync_PostsMarkdownToParentAndReturnsCreatedId()
    {
        const string markdown = "  **Keep markdown**\n\n    code  ";
        var rawSender = new QueueRequestSender(new ResponseMessage(
            201,
            true,
            new Dictionary<string, string>(),
            "{\"id\":98}"));
        var authenticatedSender = new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token")));
        var service = new BitbucketPullRequestCommentService(authenticatedSender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var createdId = await service.CreateReplyAsync(
            "team space",
            "repo name",
            42,
            17,
            markdown,
            cancellationTokenSource.Token);

        Assert.Equal(98, createdId);
        var sent = Assert.Single(rawSender.Requests);
        Assert.Equal(HttpMethodType.Post, sent.Request.Method);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/pullrequests/42/comments",
            sent.Request.Endpoint.AbsoluteUri);
        Assert.Equal("application/json", sent.Request.Headers?["Accept"]);
        Assert.Equal("application/json", sent.Request.Body?.ContentType);
        Assert.Equal(cancellationTokenSource.Token, sent.CancellationToken);
        var credential = Assert.IsType<BearerTokenCredential>(sent.Request.Credential);
        Assert.Equal("access-token", credential.Token);

        using var body = JsonDocument.Parse(sent.Request.Body!.Content);
        Assert.Equal(markdown, body.RootElement.GetProperty("content").GetProperty("raw").GetString());
        Assert.Equal(17, body.RootElement.GetProperty("parent").GetProperty("id").GetInt64());
    }

    [Theory]
    [InlineData(500, false, "{\"id\":98}")]
    [InlineData(201, true, null)]
    [InlineData(201, true, "")]
    [InlineData(201, true, "not-json")]
    [InlineData(201, true, "{}")]
    [InlineData(201, true, "{\"id\":0}")]
    public async Task CreateReplyAsync_WhenResponseIsInvalid_ThrowsCommentException(
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

        await Assert.ThrowsAsync<BitbucketPullRequestCommentException>(() => service.CreateReplyAsync(
            "team",
            "repo",
            42,
            17,
            "Reply",
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateReplyAsync_WhenArgumentsAreInvalid_RejectsBeforeSending()
    {
        var sender = new QueueRequestSender();
        var service = new BitbucketPullRequestCommentService(sender);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateReplyAsync(
            " ", "repo", 42, 17, "Reply", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateReplyAsync(
            "team", " ", 42, 17, "Reply", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateReplyAsync(
            "team", "repo", 0, 17, "Reply", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateReplyAsync(
            "team", "repo", 42, 0, "Reply", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateReplyAsync(
            "team", "repo", 42, 17, "  \r\n ", CancellationToken.None));
        Assert.Empty(sender.Requests);
    }

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
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments?pagelen=100",
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
            "https://api.bitbucket.org/2.0/repositories/team%20space/repo%20name/pullrequests/7/comments?pagelen=100",
            request.Endpoint.AbsoluteUri);
        var credential = Assert.IsType<BearerTokenCredential>(request.Credential);
        Assert.Equal("access-token", credential.Token);
    }

    [Fact]
    public async Task GetUnresolvedForFileAsync_ReturnsMatchingThreadsWithReplies()
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
                      "created_on": "2026-08-01T14:00:00Z"
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

        Assert.Equal([11L, 12L, 14L], result.Select(comment => comment.Id));
        Assert.Equal(10, result[0].FromLine);
        Assert.Equal(212, result[1].ToLine);
        Assert.Equal(12, result[2].ParentId);
        Assert.Equal("src/UseCase.cs", result[2].FilePath);
        Assert.Equal(212, result[2].ToLine);
        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team/repo/pullrequests/42/comments?pagelen=100",
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

    [Fact]
    public async Task GetUnresolvedForFileAsync_AfterGetAll_ReusesCachedComments()
    {
        var sender = new QueueRequestSender(Success("{\"values\":[]}"));
        var service = new BitbucketPullRequestCommentService(sender);

        await service.GetAllAsync("team", "repo", 42, CancellationToken.None);
        await service.GetUnresolvedForFileAsync("team", "repo", 42, "src/File.cs", CancellationToken.None);

        Assert.Single(sender.Requests);
    }

    [Fact]
    public async Task GetAllAsync_AfterCreateReply_ReloadsComments()
    {
        var sender = new QueueRequestSender(
            Success("{\"values\":[]}"),
            Success("{\"id\":98}"),
            Success("{\"values\":[]}"));
        var service = new BitbucketPullRequestCommentService(sender);

        await service.GetAllAsync("team", "repo", 42, CancellationToken.None);
        await service.CreateReplyAsync("team", "repo", 42, 17, "respuesta", CancellationToken.None);
        await service.GetAllAsync("team", "repo", 42, CancellationToken.None);

        Assert.Equal(3, sender.Requests.Count);
        Assert.Equal(HttpMethodType.Get, sender.Requests[2].Request.Method);
    }

    [Fact]
    public async Task GetAllAsync_WhenCacheExpires_ReloadsComments()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var sender = new QueueRequestSender(Success("{\"values\":[]}"), Success("{\"values\":[]}"));
        var service = new BitbucketPullRequestCommentService(sender, timeProvider);

        await service.GetAllAsync("team", "repo", 42, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await service.GetAllAsync("team", "repo", 42, CancellationToken.None);
        Assert.Single(sender.Requests);

        timeProvider.Advance(TimeSpan.FromSeconds(31));
        await service.GetAllAsync("team", "repo", 42, CancellationToken.None);
        Assert.Equal(2, sender.Requests.Count);
    }

    private static ResponseMessage Success(string content) =>
        new(200, true, new Dictionary<string, string>(), content);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void Advance(TimeSpan duration) => _utcNow += duration;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
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
