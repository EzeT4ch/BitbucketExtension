using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.Shared.Layout;

internal abstract class ThemedRemoteUserControl : RemoteUserControl
{
    private const string SharedLayoutResource =
        "ClientExtension.Presentation.Shared.Layout.RemoteUiLayout.xaml";

    protected ThemedRemoteUserControl(object dataContext)
        : base(dataContext)
    {
        ResourceDictionaries.AddEmbeddedResource(SharedLayoutResource);
    }
}
