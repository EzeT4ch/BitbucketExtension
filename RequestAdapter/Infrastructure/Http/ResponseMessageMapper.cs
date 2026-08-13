using RequestAdapter.Domain.Requests;

namespace RequestAdapter.Infrastructure.Http;

internal static class ResponseMessageMapper
{
    public static async Task<ResponseMessage> ToResponseMessageAsync(
        HttpResponseMessage httpResponseMessage,
        ResponseBodyMode responseBodyMode,
        CancellationToken cancellationToken)
    {
        var headers = httpResponseMessage.Headers
            .Concat(httpResponseMessage.Content.Headers)
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value));

        var contentBytes = responseBodyMode == ResponseBodyMode.Binary
            ? await httpResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken)
            : null;
        var content = responseBodyMode == ResponseBodyMode.Text
            ? await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken)
            : null;

        return new ResponseMessage(
            (int)httpResponseMessage.StatusCode,
            httpResponseMessage.IsSuccessStatusCode,
            headers,
            content,
            contentBytes);
    }
}
