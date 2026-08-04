namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketPullRequestCommentException : BitbucketAdapterException
{
    public BitbucketPullRequestCommentException(string message)
        : base(message)
    {
    }

    public BitbucketPullRequestCommentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
