namespace RequestAdapter.Domain.Exceptions;

public sealed class RequestTimeoutException : RequestAdapterException
{
    public RequestTimeoutException(string message) : base(message)
    {
    }
}