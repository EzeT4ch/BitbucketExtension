namespace RequestAdapter.Domain.Exceptions;

public sealed class RequestTransportException : RequestAdapterException
{
    public RequestTransportException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
