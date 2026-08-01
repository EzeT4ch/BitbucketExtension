namespace BitbucketAdapter.Domain.Exceptions;

public abstract class BitbucketAdapterException : Exception
{
    protected BitbucketAdapterException(string message) : base(message)
    {
    }

    protected BitbucketAdapterException(string message, Exception innerException) : base(message, innerException)
    {
    }
}