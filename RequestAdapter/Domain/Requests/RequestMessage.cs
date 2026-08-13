using RequestAdapter.Domain.Auth;

namespace RequestAdapter.Domain.Requests;

public sealed record RequestMessage(
    HttpMethodType Method,
    Uri Endpoint,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyDictionary<string, string>? QueryParameters = null,
    RequestBody? Body = null,
    TimeSpan? Timeout = null,
    AuthCredential? Credential = null,
    ResponseBodyMode ResponseBodyMode = ResponseBodyMode.Text);
