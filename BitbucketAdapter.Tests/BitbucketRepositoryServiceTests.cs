using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.Repositories;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Application.Auth;
using RequestAdapter.Domain.Auth;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketRepositoryServiceTests
{
    [Fact]
    public async Task GetAllAsync_FollowsPaginationAndReturnsSortedUniqueRepositories()
    {
        var sender = new QueueRequestSender(
            Success("""
                {
                  "values": [
                    { "uuid": "{z}", "slug": "zeta-api", "name": "Zeta" },
                    { "uuid": "{a}", "slug": "alpha-api", "name": "Alpha" }
                  ],
                  "next": "https://api.bitbucket.org/2.0/repositories/team-workspace?page=2"
                }
                """),
            Success("""
                {
                  "values": [
                    { "uuid": "{a-duplicate}", "slug": "ALPHA-API", "name": "Alpha duplicate" },
                    { "uuid": "{b}", "slug": "beta-api", "name": "Beta" }
                  ]
                }
                """));
        var service = new BitbucketRepositoryService(sender);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.GetAllAsync("team-workspace", cancellationTokenSource.Token);

        Assert.Equal(["alpha-api", "beta-api", "zeta-api"], result.Select(repository => repository.Slug));
        Assert.Equal(2, sender.Requests.Count);
        Assert.Equal(
            new Uri("https://api.bitbucket.org/2.0/repositories/team-workspace"),
            sender.Requests[0].Request.Endpoint);
        Assert.Equal(
            new Uri("https://api.bitbucket.org/2.0/repositories/team-workspace?page=2"),
            sender.Requests[1].Request.Endpoint);
        Assert.Equal("application/json", sender.Requests[0].Request.Headers?["Accept"]);
        Assert.All(sender.Requests, sent => Assert.Equal(cancellationTokenSource.Token, sent.CancellationToken));
    }

    [Fact]
    public async Task GetAllAsync_EscapesWorkspaceSlugInEndpoint()
    {
        var sender = new QueueRequestSender(Success("{\"values\":[]}"));
        var service = new BitbucketRepositoryService(sender);

        await service.GetAllAsync("team workspace", CancellationToken.None);

        Assert.Equal(
            "https://api.bitbucket.org/2.0/repositories/team%20workspace",
            sender.Requests.Single().Request.Endpoint.AbsoluteUri);
    }

    [Fact]
    public async Task GetAllAsync_UsesCredentialAddedByAuthenticatedSender()
    {
        var rawSender = new QueueRequestSender(Success("{\"values\":[]}"));
        var authenticatedSender = new AuthenticatedHttpRequestSender(
            rawSender,
            new StubAuthenticationProvider(new BearerTokenCredential("access-token")));
        var service = new BitbucketRepositoryService(authenticatedSender);

        await service.GetAllAsync("team-workspace", CancellationToken.None);

        var credential = Assert.IsType<BearerTokenCredential>(rawSender.Requests.Single().Request.Credential);
        Assert.Equal("access-token", credential.Token);
    }

    [Fact]
    public async Task GetAllAsync_WhenNoRepositoriesAreAvailable_ReturnsEmptyCollection()
    {
        var service = new BitbucketRepositoryService(
            new QueueRequestSender(Success("{\"values\":[]}")));

        var result = await service.GetAllAsync("team-workspace", CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(500, false, "{\"values\":[]}")]
    [InlineData(200, true, null)]
    [InlineData(200, true, "")]
    [InlineData(200, true, "not-json")]
    [InlineData(200, true, "{}")]
    [InlineData(200, true, "{\"values\":[{\"uuid\":\"{id}\",\"slug\":\"repo\"}]}")]
    public async Task GetAllAsync_WhenResponseIsInvalid_ThrowsRepositoryException(
        int statusCode,
        bool isSuccessStatusCode,
        string? content)
    {
        var service = new BitbucketRepositoryService(
            new QueueRequestSender(new ResponseMessage(
                statusCode,
                isSuccessStatusCode,
                new Dictionary<string, string>(),
                content)));

        await Assert.ThrowsAsync<BitbucketRepositoryException>(
            () => service.GetAllAsync("team-workspace", CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAsync_WhenNextLinkTargetsAnotherHost_ThrowsRepositoryException()
    {
        var service = new BitbucketRepositoryService(
            new QueueRequestSender(Success("""
                {
                  "values": [],
                  "next": "https://example.com/2.0/repositories/team-workspace?page=2"
                }
                """)));

        await Assert.ThrowsAsync<BitbucketRepositoryException>(
            () => service.GetAllAsync("team-workspace", CancellationToken.None));
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
