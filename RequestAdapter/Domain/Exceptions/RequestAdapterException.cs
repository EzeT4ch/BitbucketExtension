namespace RequestAdapter.Domain.Exceptions;

public abstract class RequestAdapterException : Exception
{
    protected RequestAdapterException(string message) : base(message)
    {
    }

    protected RequestAdapterException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
