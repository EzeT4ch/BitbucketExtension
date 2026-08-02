using BitbucketAdapter.Application.Abstractions;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Application.Connection;

public sealed class BitbucketConnectionStatusService : IBitbucketConnectionStatusService
{
    private static readonly Uri CurrentUserEndpoint = new("https://api.bitbucket.org/2.0/user");

    private readonly IHttpRequestSender _requestSender;

    public BitbucketConnectionStatusService(IHttpRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<BitbucketConnectionStatus> GetAsync(CancellationToken cancellationToken)
    {
        var request = new RequestMessage(HttpMethodType.Get, CurrentUserEndpoint);
        var response = await _requestSender.SendAsync(request, cancellationToken);
        return new BitbucketConnectionStatus(response.StatusCode);
    }
}
