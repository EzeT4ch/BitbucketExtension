namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketPullRequestChangesException : BitbucketAdapterException
{
    public BitbucketPullRequestChangesException(string message)
        : base(message)
    {
    }

    public BitbucketPullRequestChangesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
