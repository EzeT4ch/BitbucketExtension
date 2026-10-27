namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketUserException : BitbucketAdapterException
{
    public BitbucketUserException(string message)
        : base(message)
    {
    }

    public BitbucketUserException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
