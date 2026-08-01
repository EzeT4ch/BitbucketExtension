using RequestAdapter.Domain.Requests;

namespace RequestAdapter.Infrastructure.Http;

internal static class ResponseMessageMapper
{
    public static async Task<ResponseMessage> ToResponseMessageAsync(
        HttpResponseMessage httpResponseMessage,
        CancellationToken cancellationToken)
    {
        var headers = httpResponseMessage.Headers
            .Concat(httpResponseMessage.Content.Headers)
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value));

        var content = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);

        return new ResponseMessage(
            (int)httpResponseMessage.StatusCode,
            httpResponseMessage.IsSuccessStatusCode,
            headers,
            content);
    }
}