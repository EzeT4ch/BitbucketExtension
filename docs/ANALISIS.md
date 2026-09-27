# Análisis de la extensión — estado, mejoras y faltantes

> Fecha: septiembre 2026 · Base: rama `develop` (commit `159cc96`) + quick wins de este análisis.

La extensión promete **gestionar Pull Requests de Bitbucket e Historias de Usuario de Jira desde Visual Studio, en versiones Cloud y Data Center**. Hoy cubre una parte sólida de la **lectura de PR en Bitbucket Cloud** y la respuesta a comentarios. Jira y Data Center no están iniciados.

## 1. Qué existe hoy

| Proyecto | Estado | Contenido |
|---|---|---|
| `RequestAdapter` | Funcional | Transporte HTTP (`HttpClientRequestSender`), decorador de credenciales (`AuthenticatedHttpRequestSender`), timeouts, excepciones propias. |
| `BitbucketAdapter` | Funcional (solo Cloud) | Auth App Password (DPAPI) y OAuth 2.0 con loopback y refresh; reintento en 401. Workspaces, repositorios (selección persistida), PR abiertos, detalle, diffstat, diff por archivo, contenido antes/después, comentarios (listar, por archivo, responder, imágenes). |
| `ClientExtensión` | Funcional | VS.Extensibility out-of-proc + Remote UI. Comandos en menú *Extensiones* (auth, estado, borrar token, abrir ventana). Tool window con workspace → repo → lista de PR → detalle (árbol de archivos, diff unificado con resaltado C#, comentarios inline con respuestas e imágenes, pestañas Info / Comentarios / Tareas / Actividad) y botón de diff nativo. |
| `JiraAdapter` | Vacío | Solo el `.csproj`. |
| `AuthTestConsole` | Utilidad | Harness manual para probar los flujos de auth. |
| Tests | ~120 casos | `BitbucketAdapter.Tests` (net8.0) y `ClientExtension.Tests` (net8.0-windows). |

**Placeholders detectados**
- Pestañas *Tareas* y *Actividad*: sólo muestran un conteo y fechas (`PullRequestDetailData.TaskSummary` / `ActivitySummary`).
- String resource `ClientExtension.ShowIntegrationHubCommand.DisplayName` sin comando asociado.
- *(Eliminado en este análisis)* una segunda tool window de diff (`Presentation/Bitbucket/PullRequests/Diff/*`) que estaba registrada pero nunca se abría.

## 2. Mejoras de UI

| # | Problema | Propuesta | Esfuerzo |
|---|---|---|---|
| U1 | Descripción de PR y comentarios se muestran como texto plano: el markdown (listas, código, links) no se renderiza y los links no son clickeables. | Parser markdown mínimo a bloques (párrafo, código, lista, link) reutilizando el modelo de `ContentBlocks` que ya existe para imágenes. | M |
| U2 | No hay botón **Refrescar**: para ver PR/comentarios nuevos hay que cambiar de repositorio. | Comando *Refrescar* en lista de PR y en el detalle (invalida la caché de comentarios). | S |
| U3 | Lista de PR sin filtros ni búsqueda. | Filtros "Mis PR", "Soy reviewer", estado (OPEN/MERGED/DECLINED) y búsqueda por texto/rama. | M |
| U4 | Estados de carga sólo como texto. | `ProgressBar` indeterminada ligada a un `IsLoading` por sección. | S |
| U5 | La tool window abre **flotante**. | `ToolWindowPlacement.DocumentWell` o acoplada a la derecha por defecto. | XS |
| U6 | "Diff nativo" ejecuta `devenv.exe /Diff`, que abre **otra instancia** de Visual Studio. | Abrir los temporales en la instancia actual o, si VS.Extensibility no expone el comparador, quitar el botón y mejorar el diff propio (vista dividida). | M |
| U7 | Resaltado de sintaxis sólo para C#. | Tokenizer liviano por extensión (JSON, XML/XAML, TS/JS, SQL) o sin color pero con numeración legible. | M |
| U8 | Reviewers sin estado; sin build status. | Badges "aprobó / pidió cambios" y estado de pipelines. | S |
| U9 | Comentarios: sólo se puede responder. | Comentario nuevo (general e inline en la línea del diff), editar, borrar, resolver/reabrir. | L |
| U10 | `PullRequestSelectionControl.xaml` (~990 líneas) repite 3 veces la plantilla de comentario/respuesta. | Extraer `DataTemplate`s a un diccionario en `Presentation/Shared/Layout`. | S |

## 3. Optimización

| # | Problema | Acción | Estado |
|---|---|---|---|
| O1 | Listados sin `pagelen`: Bitbucket devuelve 10 ítems por página → muchas vueltas de red. | `pagelen` máximo por endpoint: PR 50, comentarios 100, repos 100, workspaces 100, diffstat 500. | ✅ Hecho |
| O2 | Comentarios descargados N+1 veces: al elegir un PR se traen todos y **cada clic en un archivo** vuelve a paginar todos. | Caché por PR en `BitbucketPullRequestCommentService` (TTL 60 s, se invalida al responder). | ✅ Hecho |
| O3 | Imágenes de comentarios re-descargadas en cada clic (la caché en disco sólo evitaba reescribir). | Lookup en memoria URL → archivo local antes de descargar (`PullRequestCommentImageCache.TryGetLocalUrl`). | ✅ Hecho |
| O4 | Se creaba un `HttpClient` nuevo por cada construcción del proveedor OAuth. | `HttpClient` compartido en `BitbucketCompositionRoot`. | ✅ Hecho |
| O5 | Código muerto (tool window de diff sin uso, recursos XAML embebidos). | Eliminado. | ✅ Hecho |
| O6 | `https://api.bitbucket.org/` y el parser de paginación están copiados en 6 servicios. | `BitbucketApiOptions` (base URL) + helper de paginación común. **Prerrequisito de Data Center.** | Pendiente |
| O7 | Respuestas completas en listados. | `fields=` para pedir sólo lo necesario; `ETag`/`If-None-Match` donde aplique. | Pendiente |
| O8 | `Microsoft.CodeAnalysis.CSharp` (Roslyn) se carga sólo para colorear. | Tokenizer liviano o carga diferida. | Pendiente |
| O9 | Versiones mezcladas: `Microsoft.Extensions.Configuration` 8.0 vs 9.0; `AuthTestConsole` en net10 y el resto en net8; el `.slnx` requiere SDK ≥ 9.0.200. | Unificar versiones y documentar el SDK mínimo (`global.json`). | Pendiente |
| O10 | No hay reintentos ante 429 (rate limit) ni 5xx; sólo se reintenta una vez ante 401. | Decorador de reintento con backoff respetando `Retry-After`. | Pendiente |

## 4. Faltantes para cumplir la promesa (roadmap priorizado)

### Fase 1 — Bitbucket Cloud completo *(prioridad)*
1. **Acciones sobre el PR**: aprobar / quitar aprobación, pedir cambios, declinar, merge (con estrategia y cierre de rama) → nuevos métodos en `IBitbucketPullRequestService`.
2. **Comentarios**: nuevo general e inline, editar, borrar, resolver/reabrir (API `comments` + `resolve`).
3. **Tareas** reales (`/pullrequests/{id}/tasks`): listar, crear, marcar hecha — reemplaza el placeholder.
4. **Actividad** real (`/pullrequests/{id}/activity`) y **commits** del PR (`/commits`).
5. **Build statuses** (`/commit/{hash}/statuses`) y estado de reviewers (`participants`).
6. Lista: filtros, PR no abiertos, refresco, **crear PR** desde la rama actual.
7. **Autenticación**: Atlassian deprecó las App Passwords (deshabilitadas desde junio 2026). Agregar **API token** (Basic email + token) como reemplazo y definir cómo se distribuye el OAuth consumer (hoy depende de *user-secrets* del desarrollador, no apto para usuarios finales).

### Fase 2 — Jira
- `JiraAdapter` con capas `Domain/Application/Infrastructure` sobre `RequestAdapter`.
- Auth: API token (Cloud) / PAT (Data Center).
- Buscar HU por JQL, ver detalle, transicionar estado, comentar.
- Vincular PR ↔ HU por clave en rama/título; mostrar la HU dentro del detalle del PR.
- UI propia bajo `Presentation/Jira/` (según `ClientExtensión/ARCHITECTURE.md`).

### Fase 3 — Data Center (Bitbucket y Jira)
- Base URL configurable (depende de O6).
- Bitbucket DC usa otra API (`rest/api/1.0`, proyecto/repo en vez de workspace, paginación `start/limit/isLastPage`, PAT): implementar los contratos de `Application/Abstractions` con una variante DC y elegirla por configuración.

### Transversal
- CI (build + tests en Windows), empaquetado VSIX y publicación en Marketplace.
- Página de opciones (instancia, método de auth, filtros por defecto).
- Logging/diagnóstico (panel de salida), manejo de rate limits.
- Documentación de usuario y capturas.

## 5. Cambios aplicados junto a este análisis
- `pagelen` en los endpoints de listado (O1) — tests actualizados.
- Caché de comentarios por PR con invalidación al responder (O2) — 3 tests nuevos.
- Reuso de imágenes ya descargadas (O3).
- `HttpClient` compartido (O4).
- Eliminación de la tool window de diff sin uso (O5).
- `CLAUDE.md` y `AGENTS.md` actualizados al estado real del repo.

**Nota:** con la caché de comentarios, volver a seleccionar el mismo PR dentro de los 60 s muestra los comentarios cacheados; se resolverá con el botón Refrescar (U2).
