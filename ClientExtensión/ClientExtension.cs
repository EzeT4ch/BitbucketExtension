using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using ClientExtension.Composition;

namespace ClientExtension;

[VisualStudioContribution]
internal class ClientExtensionEntrypoint : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "ClientExtension.9d3f7f7a-9b7a-4e8a-9d0a-9b8b8b8f1d4c",
            version: this.ExtensionAssemblyVersion,
            publisherName: "S.Sotelo, E.Benitez",
            displayName: "Bitbucket/Jira Client Extension",
            description: "Gestión de Pull Requests de Bitbucket e Historias de Usuario de Jira desde Visual Studio."),
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddBitbucketServices();
    }
}
