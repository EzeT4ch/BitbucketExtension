using System.Net;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace BitbucketAdapter.Application.Auth;

public sealed class UnauthorizedRetryHttpRequestSender : IHttpRequestSender
{
    private readonly IHttpRequestSender _innerSender;
    private readonly BitbucketCredentialProvider _credentialProvider;

    public UnauthorizedRetryHttpRequestSender(IHttpRequestSender innerSender, BitbucketCredentialProvider credentialProvider)
    {
        _innerSender = innerSender;
        _credentialProvider = credentialProvider;
    }

    public async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _innerSender.SendAsync(request, cancellationToken);
        if (response.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return response;
        }

        await _credentialProvider.InvalidateCurrentCredentialAsync(cancellationToken);
        return await _innerSender.SendAsync(request, cancellationToken);
    }
}