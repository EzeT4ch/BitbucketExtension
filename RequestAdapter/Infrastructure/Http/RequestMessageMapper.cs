using System.Net.Http.Headers;
using System.Web;
using RequestAdapter.Domain.Requests;

namespace RequestAdapter.Infrastructure.Http;

internal static class RequestMessageMapper
{
    public static HttpRequestMessage ToHttpRequestMessage(RequestMessage request)
    {
        var httpRequestMessage = new HttpRequestMessage(ToHttpMethod(request.Method), BuildUri(request));

        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (request.Body is not null)
        {
            httpRequestMessage.Content = new StringContent(request.Body.Content);
            httpRequestMessage.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(request.Body.ContentType);
        }

        if (request.Credential is not null)
        {
            AuthCredentialHttpMapper.Apply(httpRequestMessage, request.Credential);
        }

        return httpRequestMessage;
    }

    private static Uri BuildUri(RequestMessage request)
    {
        if (request.QueryParameters is null || request.QueryParameters.Count == 0)
        {
            return request.Endpoint;
        }

        var query = HttpUtility.ParseQueryString(request.Endpoint.Query);
        foreach (var parameter in request.QueryParameters)
        {
            query[parameter.Key] = parameter.Value;
        }

        var builder = new UriBuilder(request.Endpoint) { Query = query.ToString() };
        return builder.Uri;
    }

    private static HttpMethod ToHttpMethod(HttpMethodType method) => method switch
    {
        HttpMethodType.Get => HttpMethod.Get,
        HttpMethodType.Post => HttpMethod.Post,
        HttpMethodType.Put => HttpMethod.Put,
        HttpMethodType.Patch => HttpMethod.Patch,
        HttpMethodType.Delete => HttpMethod.Delete,
        HttpMethodType.Head => HttpMethod.Head,
        HttpMethodType.Options => HttpMethod.Options,
        _ => throw new NotSupportedException($"HTTP method '{method}' is not supported.")
    };
}