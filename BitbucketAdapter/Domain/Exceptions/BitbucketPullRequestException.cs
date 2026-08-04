namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketPullRequestException : BitbucketAdapterException
{
    public BitbucketPullRequestException(string message)
        : base(message)
    {
    }

    public BitbucketPullRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
