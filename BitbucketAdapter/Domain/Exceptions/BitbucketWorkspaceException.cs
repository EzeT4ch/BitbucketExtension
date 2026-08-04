namespace BitbucketAdapter.Domain.Exceptions;

public sealed class BitbucketWorkspaceException : BitbucketAdapterException
{
    public BitbucketWorkspaceException(string message) : base(message)
    {
    }

    public BitbucketWorkspaceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
