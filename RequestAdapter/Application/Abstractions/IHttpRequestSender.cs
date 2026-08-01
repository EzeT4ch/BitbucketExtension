using RequestAdapter.Domain.Requests;

namespace RequestAdapter.Application.Abstractions;

public interface IHttpRequestSender
{
    Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken);
}
