using System.Diagnostics;
using System.Net;
using System.Text;
using BitbucketAdapter.Application.Abstractions.OAuth;
using BitbucketAdapter.Domain.Auth.OAuth;
using BitbucketAdapter.Domain.Exceptions;
using BitbucketAdapter.Infrastructure.Auth.OAuth;

namespace ClientExtension.Composition.OAuth;

internal sealed class BitbucketLoopbackAuthorizationCodeProvider : IAuthorizationCodeProvider
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(3);

    private readonly OAuthConsumer _consumer;

    public BitbucketLoopbackAuthorizationCodeProvider(OAuthConsumer consumer)
    {
        _consumer = consumer;
    }

    public async Task<string> GetAuthorizationCodeAsync(CancellationToken cancellationToken)
    {
        var redirectUri = new Uri(_consumer.RedirectUri);
        var listenerPrefix = $"{redirectUri.Scheme}://{redirectUri.Authority}/";

        using var httpListener = new HttpListener();
        httpListener.Prefixes.Add(listenerPrefix);

        try
        {
            httpListener.Start();
        }
        catch (HttpListenerException exception)
        {
            throw new BitbucketAuthenticationException(
                $"No se pudo iniciar el listener local en {listenerPrefix} para completar el login de Bitbucket.",
                exception);
        }

        try
        {
            var state = Guid.NewGuid().ToString("N");
            var authorizationUri = BitbucketOAuthAuthorizationUrlBuilder.Build(_consumer, state);
            OpenInDefaultBrowser(authorizationUri);

            using var timeoutCts = new CancellationTokenSource(LoginTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            HttpListenerContext context;
            try
            {
                context = await httpListener.GetContextAsync().WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BitbucketAuthenticationException(
                    "Se agotó el tiempo de espera para completar el login de Bitbucket en el navegador.");
            }

            var parameters = ParseQueryString(context.Request.Url?.Query);
            var hasError = parameters.ContainsKey("error");
            await RespondAsync(context, hasError);

            if (parameters.TryGetValue("error", out var error))
            {
                throw new BitbucketAuthenticationException($"Bitbucket denegó la autorización: {error}");
            }

            if (!parameters.TryGetValue("state", out var returnedState) || returnedState != state)
            {
                throw new BitbucketAuthenticationException("La respuesta de autorización de Bitbucket no incluyó el estado esperado.");
            }

            if (!parameters.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                throw new BitbucketAuthenticationException("La respuesta de autorización de Bitbucket no incluyó un código.");
            }

            return code;
        }
        finally
        {
            httpListener.Stop();
        }
    }

    private static void OpenInDefaultBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });

    private static async Task RespondAsync(HttpListenerContext context, bool isError)
    {
        var message = isError
            ? "No se pudo completar la autorización con Bitbucket. Puede cerrar esta ventana."
            : "Autorización completada. Puede cerrar esta ventana y volver a Visual Studio.";
        var buffer = Encoding.UTF8.GetBytes($"<html><body><p>{message}</p></body></html>");

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();
    }

    private static Dictionary<string, string> ParseQueryString(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var key = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var value = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return result;
    }
}
