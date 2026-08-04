namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketRepositoryException : BitbucketAdapterException
{
    public BitbucketRepositoryException(string message)
        : base(message)
    {
    }

    public BitbucketRepositoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
