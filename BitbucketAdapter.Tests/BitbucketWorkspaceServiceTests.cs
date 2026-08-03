using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.Workspaces;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketWorkspaceServiceTests
{
    [Fact]
    public async Task GetAllAsync_FollowsPaginationAndReturnsSortedUniqueWorkspaces()
    {
        var sender = new QueueRequestSender(
            Success("""
                {
                  "values": [
                    { "workspace": { "uuid": "{z}", "slug": "zeta" } },
                    { "workspace": { "uuid": "{a}", "slug": "alpha" } }
                  ],
                  "next": "https://api.bitbucket.org/2.0/user/workspaces?page=2"
                }
                """),
            Success("""
                {
                  "values": [
                    { "workspace": { "uuid": "{a-duplicate}", "slug": "ALPHA" } },
                    { "workspace": { "uuid": "{b}", "slug": "beta" } }
                  ]
                }
                """));
        var service = new BitbucketWorkspaceService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetAllAsync(cancellationTokenSource.Token);

        Assert.Equal(["alpha", "beta", "zeta"], result.Select(workspace => workspace.Slug));
        Assert.Equal(2, sender.Requests.Count);
        Assert.Equal(
            new Uri("https://api.bitbucket.org/2.0/user/workspaces"),
            sender.Requests[0].Request.Endpoint);
        Assert.Equal(
            new Uri("https://api.bitbucket.org/2.0/user/workspaces?page=2"),
            sender.Requests[1].Request.Endpoint);
        Assert.Equal("application/json", sender.Requests[0].Request.Headers?["Accept"]);
        Assert.All(sender.Requests, sent => Assert.Equal(cancellationTokenSource.Token, sent.CancellationToken));
    }

    [Fact]
    public async Task GetAllAsync_UsesCredentialAddedByAuthenticatedSender()
    {
        var rawSender = new QueueRequestSender(Success("{\"values\":[]}"));
        var authenticatedSender = new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token")));
        var service = new BitbucketWorkspaceService(authenticatedSender);

        await service.GetAllAsync(CancellationToken.None);

        var credential = Assert.IsType<BearerTokenCredential>(rawSender.Requests.Single().Request.Credential);
        Assert.Equal("access-token", credential.Token);
    }

    [Fact]
    public async Task GetAllAsync_WhenNoWorkspacesAreAvailable_ReturnsEmptyCollection()
    {
        var service = new BitbucketWorkspaceService(
            new QueueRequestSender(Success("{\"values\":[]}")));

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(500, false, "{\"values\":[]}")]
    [InlineData(200, true, null)]
    [InlineData(200, true, "")]
    [InlineData(200, true, "not-json")]
    [InlineData(200, true, "{}")]
    public async Task GetAllAsync_WhenResponseIsInvalid_ThrowsWorkspaceException(
        int statusCode,
        bool isSuccessStatusCode,
        string? content)
    {
        var service = new BitbucketWorkspaceService(
            new QueueRequestSender(new ResponseMessage(
                statusCode,
                isSuccessStatusCode,
                new Dictionary<string, string>(),
                content)));

        await Assert.ThrowsAsync<BitbucketWorkspaceException>(
            () => service.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAsync_WhenNextLinkTargetsAnotherHost_ThrowsWorkspaceException()
    {
        var service = new BitbucketWorkspaceService(
            new QueueRequestSender(Success("""
                {
                  "values": [],
                  "next": "https://example.com/2.0/user/workspaces?page=2"
                }
                """)));

        await Assert.ThrowsAsync<BitbucketWorkspaceException>(
            () => service.GetAllAsync(CancellationToken.None));
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
