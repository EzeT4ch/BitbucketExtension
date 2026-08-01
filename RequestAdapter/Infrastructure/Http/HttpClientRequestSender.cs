using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Exceptions;
using RequestAdapter.Domain.Requests;

namespace RequestAdapter.Infrastructure.Http;

public sealed class HttpClientRequestSender : IHttpRequestSender
{
    private readonly HttpClient _httpClient;

    public HttpClientRequestSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        using var httpRequestMessage = RequestMessageMapper.ToHttpRequestMessage(request);

        using var timeoutCts = request.Timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(request.Timeout!.Value);
        var effectiveToken = timeoutCts?.Token ?? cancellationToken;

        try
        {
            using var httpResponseMessage = await _httpClient.SendAsync(
                httpRequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                effectiveToken);

            return await ResponseMessageMapper.ToResponseMessageAsync(httpResponseMessage, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new RequestTransportException($"Request to '{request.Endpoint}' failed.", exception);
        }
        catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested
                                                                        && !cancellationToken.IsCancellationRequested)
        {
            throw new RequestTimeoutException($"Request to '{request.Endpoint}' timed out after {request.Timeout}.");
        }
    }
}