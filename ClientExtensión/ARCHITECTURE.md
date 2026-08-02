# Arquitectura de la extensión

La extensión se organiza por responsabilidad y por integración:

- Presentation/ contiene comandos de Visual Studio, ventanas y modelos de Remote UI. Puede depender de contratos de Application, pero no conoce implementaciones de HTTP, tokens ni almacenamiento.
- Composition/ es el único punto que une contratos de Application con implementaciones de Infrastructure y con adaptadores propios de Visual Studio.
- BitbucketAdapter/ y JiraAdapter/ encapsulan sus respectivos dominios, casos de uso e infraestructura. Los adaptadores pueden depender de RequestAdapter/, nunca al revés.

El flujo de dependencias es:

Presentation -> Application contracts <- Infrastructure/Composition

Cada nueva integración debe incorporar su pantalla y comandos bajo Presentation/<Provider>/, y exponer sus operaciones a la UI mediante contratos de Application. No debe realizar llamadas HTTP directas desde la extensión.
