using BitbucketAdapter.Application.Abstractions.AppPassword;
using BitbucketAdapter.Domain.Auth.AppPassword;

namespace BitbucketAdapter.Application.Auth.AppPassword;

public sealed class BitbucketAppPasswordProvider
{
    private readonly IAppPasswordCredentialStore _credentialStore;
    private readonly IAppPasswordPrompt _credentialPrompt;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public BitbucketAppPasswordProvider(IAppPasswordCredentialStore credentialStore, IAppPasswordPrompt credentialPrompt)
    {
        _credentialStore = credentialStore;
        _credentialPrompt = credentialPrompt;
    }

    public async Task<BitbucketAppPasswordCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        var cachedCredential = await _credentialStore.GetAsync(cancellationToken);
        if (cachedCredential is not null)
        {
            return cachedCredential;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            cachedCredential = await _credentialStore.GetAsync(cancellationToken);
            if (cachedCredential is not null)
            {
                return cachedCredential;
            }

            var credential = await _credentialPrompt.RequestCredentialAsync(cancellationToken);
            await _credentialStore.SaveAsync(credential, cancellationToken);
            return credential;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task InvalidateCurrentCredentialAsync(CancellationToken cancellationToken) =>
        _credentialStore.ClearAsync(cancellationToken);
}
