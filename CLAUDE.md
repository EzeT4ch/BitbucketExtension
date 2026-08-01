# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Resumen del proyecto

Extensión de Visual Studio para gestionar Pull Requests de Bitbucket e Historias de Usuario (HU) de Jira sin salir del IDE. Integra las APIs REST de Bitbucket y Jira, ambas en sus versiones Cloud y Data Center.

Autores: S.Sotelo y E.Benitez. Versión actual: 0.1.0.

## Estado del repositorio

El proyecto está en una etapa muy temprana. La arquitectura objetivo tiene 4 componentes, pero hoy solo existen 2 como proyectos vacíos (`Class1.cs` sin implementar, sin subcarpetas `Domain/`, `Application/`, `Infrastructure/` todavía):

- `BitbucketAdapter/` — existe, stub vacío
- `RequestAdapter/` — existe, stub vacío
- `JiraAdapter` — planeado en el README, **no creado aún**
- `ClientExtensión` (la extensión de VS en sí) — planeado en el README, **no creado aún**

No asumir la existencia de código, capas o funcionalidad más allá de lo que realmente está en el repo.

## Comandos

- Restaurar dependencias: `dotnet restore CommentExtensionBitbucket.slnx`
- Compilar la solución: `dotnet build CommentExtensionBitbucket.slnx`

Notas:
- `CommentExtensionBitbucket.slnx` usa el nuevo formato de solución XML (`.slnx`), no `.sln`.
- No hay proyectos de test, linter ni CI configurados todavía.

## Arquitectura

Cada adaptador (`BitbucketAdapter`, `RequestAdapter`, y el futuro `JiraAdapter`) sigue arquitectura por capas dentro del propio proyecto, según el README:

- `Domain/` — modelo de dominio, sin dependencias de infraestructura
- `Application/` — casos de uso
- `Infrastructure/` — implementación concreta (HTTP, clientes de API, etc.)

Roles previstos por proyecto:
- `RequestAdapter` — cliente HTTP genérico, consumido por los adaptadores de API
- `BitbucketAdapter` — adaptador a la API REST de Bitbucket
- `JiraAdapter` (futuro) — adaptador a la API REST de Jira
- `ClientExtensión` (futuro) — extensión de Visual Studio que consume los adaptadores anteriores

Todos los proyectos usan `TargetFramework net10.0` con `Nullable` e `ImplicitUsings` habilitados.

## TEMPLATE.md

`TEMPLATE.md` es la plantilla del repo para describir una tarea/feature (Problema, Tareas, Referencias, Request de ejemplo). Si el usuario pega contenido con ese formato, es la descripción de un trabajo a implementar, no documentación del propio repositorio.
