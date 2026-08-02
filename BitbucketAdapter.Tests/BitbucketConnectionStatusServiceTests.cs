using BitbucketAdapter.Application.Abstractions;
using BitbucketAdapter.Application.Connection;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Tests;

public sealed class BitbucketConnectionStatusServiceTests
{
    [Fact]
    public async Task GetAsync_SendsRequestToCurrentUserEndpointAndReturnsStatusCode()
    {
        var requestSender = new FakeRequestSender(new ResponseMessage(
            200,
            true,
            new Dictionary<string, string>(),
            null));
        var service = new BitbucketConnectionStatusService(requestSender);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(requestSender.LastRequest);
        Assert.Equal(HttpMethodType.Get, requestSender.LastRequest.Method);
        Assert.Equal(new Uri("https://api.bitbucket.org/2.0/user"), requestSender.LastRequest.Endpoint);
    }

    private sealed class FakeRequestSender : IHttpRequestSender
    {
        private readonly ResponseMessage _response;

        public FakeRequestSender(ResponseMessage response)
        {
            _response = response;
        }

        public RequestMessage? LastRequest { get; private set; }

        public Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }
}
