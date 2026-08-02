using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ClientExtension.Presentation.IntegrationHub;

[DataContract]
internal sealed class IntegrationHubData : NotifyPropertyChangedObject
{
    [DataMember]
    public string Title => "Centro de integraciones";

    [DataMember]
    public string Subtitle => "Un único punto de trabajo para Bitbucket y Jira.";

    [DataMember]
    public string BitbucketTitle => "Bitbucket";

    [DataMember]
    public string BitbucketDescription => "Pull requests, revisiones y comentarios del repositorio activo.";

    [DataMember]
    public string BitbucketStatus => "Próximamente: conectar datos de pull requests.";

    [DataMember]
    public string JiraTitle => "Jira";

    [DataMember]
    public string JiraDescription => "Historias de usuario, tareas y trazabilidad asociada a la rama actual.";

    [DataMember]
    public string JiraStatus => "Próximamente: conectar datos de incidencias.";

    [DataMember]
    public string NextStep => "Seleccioná una integración cuando los casos de uso estén disponibles.";
}
