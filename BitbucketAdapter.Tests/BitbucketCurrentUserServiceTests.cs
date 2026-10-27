using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.Users;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketCurrentUserServiceTests
{
    [Fact]
    public async Task GetAsync_ParsesUserAndCachesIt()
    {
        var sender = new QueueRequestSender(Response(200, """
            { "uuid": " {abc-123} ", "display_name": " Ana Pérez " }
            """));
        var service = new BitbucketCurrentUserService(sender);

        var first = await service.GetAsync(CancellationToken.None);
        var second = await service.GetAsync(CancellationToken.None);

        Assert.Equal("{abc-123}", first.Uuid);
        Assert.Equal("Ana Pérez", first.DisplayName);
        Assert.Same(first, second);
        var request = Assert.Single(sender.Requests);
        Assert.Equal(HttpMethodType.Get, request.Method);
        Assert.Equal("https://api.bitbucket.org/2.0/user", request.Endpoint.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_WhenRequestFails_ThrowsAndRetriesOnNextCall()
    {
        var sender = new QueueRequestSender(
            Response(401, "{}"),
            Response(200, "{\"uuid\":\"{abc}\"}"));
        var service = new BitbucketCurrentUserService(sender);

        var exception = await Assert.ThrowsAsync<BitbucketUserException>(
            () => service.GetAsync(CancellationToken.None));
        var user = await service.GetAsync(CancellationToken.None);

        Assert.Contains("401", exception.Message);
        Assert.Equal("{abc}", user.Uuid);
        Assert.Equal("{abc}", user.DisplayName);
    }

    [Fact]
    public async Task GetAsync_WhenUuidIsMissing_Throws()
    {
        var service = new BitbucketCurrentUserService(
            new QueueRequestSender(Response(200, "{\"display_name\":\"Ana\"}")));

        await Assert.ThrowsAsync<BitbucketUserException>(() => service.GetAsync(CancellationToken.None));
    }

    private static ResponseMessage Response(int statusCode, string content) =>
        new(statusCode, statusCode is >= 200 and < 300, new Dictionary<string, string>(), content);

    private sealed class QueueRequestSender : IHttpRequestSender
    {
        private readonly Queue<ResponseMessage> _responses;

        public QueueRequestSender(params ResponseMessage[] responses)
        {
            _responses = new Queue<ResponseMessage>(responses);
        }

        public List<RequestMessage> Requests { get; } = [];

        public Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
