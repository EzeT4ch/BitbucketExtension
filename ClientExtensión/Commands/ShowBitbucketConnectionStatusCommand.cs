using BitbucketAdapter.Domain.Exceptions;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using RequestAdapter.Application.Abstractions;
using RequestAdapter.Domain.Requests;

namespace ClientExtension.Commands;

[VisualStudioContribution]
internal class ShowBitbucketConnectionStatusCommand : Command
{
    private readonly IHttpRequestSender _sender;

    public ShowBitbucketConnectionStatusCommand(VisualStudioExtensibility extensibility, IHttpRequestSender sender)
        : base(extensibility)
    {
        _sender = sender;
    }

    public override CommandConfiguration CommandConfiguration => new("%ClientExtension.ShowBitbucketConnectionStatusCommand.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = new RequestMessage(HttpMethodType.Get, new Uri("https://api.bitbucket.org/2.0/user"));
            var response = await _sender.SendAsync(request, cancellationToken);
            await Extensibility.Shell().ShowPromptAsync(
                $"Conectado. Status: {response.StatusCode}", PromptOptions.OK, cancellationToken);
        }
        catch (BitbucketAuthenticationException exception)
        {
            await Extensibility.Shell().ShowPromptAsync(exception.Message, PromptOptions.OK, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Extensibility.Shell().ShowPromptAsync(
                $"Error inesperado al conectar con Bitbucket: {exception.Message}", PromptOptions.OK, cancellationToken);
        }
    }
}
