# Arquitectura de la extensión

La extensión se organiza por responsabilidad y por integración:

- Presentation/ contiene comandos de Visual Studio, ventanas y modelos de Remote UI. Puede depender de contratos de Application, pero no conoce implementaciones de HTTP, tokens ni almacenamiento.
- Composition/ es el único punto que une contratos de Application con implementaciones de Infrastructure y con adaptadores propios de Visual Studio.
- BitbucketAdapter/ y JiraAdapter/ encapsulan sus respectivos dominios, casos de uso e infraestructura. Los adaptadores pueden depender de RequestAdapter/, nunca al revés.

El flujo de dependencias es:

Presentation -> Application contracts <- Infrastructure/Composition

Cada nueva integración debe incorporar sus pantallas y comandos bajo `Presentation/<Provider>/<Feature>/`, y exponer sus operaciones a la UI mediante contratos de Application. Las pantallas de Bitbucket y Jira son contribuciones independientes y no deben compartir modelos de estado específicos del proveedor.

Los recursos visuales reutilizables de Remote UI viven bajo `Presentation/Shared/Layout/`. Pueden contener estilos, espaciado y recursos compatibles con el tema de Visual Studio, pero no estado ni reglas de una integración concreta. Remote UI no admite controles personalizados de la extensión dentro de XAML; por eso la reutilización visual se realiza mediante diccionarios de recursos embebidos.

Presentation no debe realizar llamadas HTTP directas ni depender de implementaciones de Infrastructure.
