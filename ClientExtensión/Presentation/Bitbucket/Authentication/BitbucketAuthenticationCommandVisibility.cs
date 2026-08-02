using BitbucketAdapter.Application.Abstractions.OAuth;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ClientExtension.Presentation.Bitbucket.Authentication;

internal sealed class BitbucketAuthenticationCommandVisibility
{
    private readonly IBitbucketOAuthConnectionService _connectionService;
    private readonly List<RegisteredCommand> _commands = [];

    public BitbucketAuthenticationCommandVisibility(IBitbucketOAuthConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task RegisterAsync(
        ICommand command,
        bool isVisibleWhenConnected,
        CancellationToken cancellationToken)
    {
        lock (_commands)
        {
            if (_commands.All(registeredCommand => registeredCommand.Command != command))
            {
                _commands.Add(new RegisteredCommand(command, isVisibleWhenConnected));
            }
        }

        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        RegisteredCommand[] commands;
        lock (_commands)
        {
            commands = [.. _commands];
        }

        var isConnected = await _connectionService.IsConnectedAsync(cancellationToken);
        foreach (var registeredCommand in commands)
        {
            registeredCommand.Command.SetVisibilityState(
                registeredCommand.IsVisibleWhenConnected == isConnected);
        }
    }

    private sealed record RegisteredCommand(ICommand Command, bool IsVisibleWhenConnected);
}
