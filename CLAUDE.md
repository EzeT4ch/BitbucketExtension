# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Resumen del proyecto

Extensión de Visual Studio para gestionar Pull Requests de Bitbucket e Historias de Usuario (HU) de Jira sin salir del IDE. Integra las APIs REST de Bitbucket y Jira, ambas en sus versiones Cloud y Data Center.

Autores: S.Sotelo y E.Benitez. Versión actual: 0.1.0.

## Estado del repositorio

Proyectos en `CommentExtensionBitbucket.slnx`:

- `RequestAdapter/` — cliente HTTP genérico (transporte, credenciales, timeouts). Funcional.
- `BitbucketAdapter/` — adaptador a la API REST de **Bitbucket Cloud** (`api.bitbucket.org` hardcodeado): auth App Password / OAuth 2.0, workspaces, repositorios, PR abiertos, detalle, diffs, comentarios (listar, responder, imágenes). Funcional.
- `JiraAdapter/` — proyecto **vacío**, sin código.
- `ClientExtensión/` — extensión de VS (VS.Extensibility out-of-proc + Remote UI; assembly/namespace `ClientExtension`). Ver `ClientExtensión/ARCHITECTURE.md`.
- `AuthTestConsole/` — harness manual de autenticación (no está en el `.slnx`).
- `BitbucketAdapter.Tests/` (net8.0) y `ClientExtension.Tests/` (net8.0-windows) — xUnit.

No hay soporte Data Center ni Jira todavía. El análisis de estado, mejoras y roadmap está en `docs/ANALISIS.md`.

## Comandos

- Restaurar: `dotnet restore CommentExtensionBitbucket.slnx`
- Compilar: `dotnet build CommentExtensionBitbucket.slnx`
- Tests del adaptador: `dotnet test BitbucketAdapter.Tests`
- Tests de la extensión: `dotnet test ClientExtension.Tests` (requiere Windows Desktop runtime)

Notas:
- `.slnx` requiere SDK .NET ≥ 9.0.200; con SDK 8 compilar por proyecto (p. ej. `dotnet build ClientExtension.Tests`).
- Fuera de Windows agregar `-p:EnableWindowsTargeting=true` para compilar los proyectos `net8.0-windows`; sus tests no corren en Linux.
- No hay linter ni CI configurados.

## Arquitectura

Cada adaptador sigue capas dentro del propio proyecto:

- `Domain/` — modelo de dominio y excepciones, sin dependencias de infraestructura
- `Application/` — casos de uso y `Abstractions/` (contratos)
- `Infrastructure/` — implementación concreta (HTTP, almacenamiento DPAPI/archivo)

Los adaptadores dependen de `RequestAdapter`, nunca al revés. En `ClientExtensión`, `Presentation/` sólo usa contratos de Application; `Composition/BitbucketCompositionRoot.cs` es el único punto que cablea implementaciones.

TFMs: adaptadores `net8.0`, extensión y sus tests `net8.0-windows`, `AuthTestConsole` `net10.0-windows`. `Nullable` e `ImplicitUsings` habilitados.

## TEMPLATE.md

`TEMPLATE.md` es la plantilla del repo para describir una tarea/feature (Problema, Tareas, Referencias, Request de ejemplo). Si el usuario pega contenido con ese formato, es la descripción de un trabajo a implementar, no documentación del propio repositorio.
