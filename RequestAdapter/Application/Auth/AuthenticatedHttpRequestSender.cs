using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace RequestAdapter.Application.Auth;

public sealed class AuthenticatedHttpRequestSender : IHttpRequestSender
{
    private readonly IHttpRequestSender _innerSender;
    private readonly IAuthenticationProvider _authenticationProvider;

    public AuthenticatedHttpRequestSender(IHttpRequestSender innerSender, IAuthenticationProvider authenticationProvider)
    {
        _innerSender = innerSender;
        _authenticationProvider = authenticationProvider;
    }

    public async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        var credential = await _authenticationProvider.GetCredentialAsync(cancellationToken);
        var authenticatedRequest = request with { Credential = credential };
        return await _innerSender.SendAsync(authenticatedRequest, cancellationToken);
    }
}
