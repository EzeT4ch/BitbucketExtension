namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketAuthenticationException : BitbucketAdapterException
{
    public BitbucketAuthenticationException(string message) : base(message)
    {
    }

    public BitbucketAuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
