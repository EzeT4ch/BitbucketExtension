namespace RequestAdapter.Domain.Requests;

public sealed record ResponseMessage(
    int StatusCode,
    bool IsSuccessStatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string? Content,
    byte[]? ContentBytes = null);
