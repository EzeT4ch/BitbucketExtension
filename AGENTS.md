# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 solution for a Visual Studio extension that integrates Bitbucket and Jira REST APIs. The solution file is `CommentExtensionBitbucket.slnx`.

- `RequestAdapter/` contains the reusable HTTP transport abstraction and implementation.
- `BitbucketAdapter/` and `JiraAdapter/` contain provider-specific API integrations.
- `ClientExtensión/` is the Visual Studio extension entry point (`ClientExtension` is its assembly and root namespace).
- `AuthTestConsole/` is a local executable for manually exercising Bitbucket authentication flows.

Adapter projects are organized into `Domain/`, `Application/`, and `Infrastructure/`. Keep domain types independent of HTTP and configuration details; application code defines use cases and abstractions; infrastructure implements API, file, and credential access. `BitbucketAdapter` and `JiraAdapter` may depend on `RequestAdapter`, not the reverse.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore CommentExtensionBitbucket.slnx  # Restore NuGet packages
dotnet build CommentExtensionBitbucket.slnx    # Compile all solution projects
dotnet run --project AuthTestConsole            # Run the manual auth harness
```

The extension targets `net10.0-windows` and may require a compatible Visual Studio installation to debug or deploy. There is currently no automated test project, linter, or CI workflow; add focused tests with non-trivial behavior changes.

## Coding Style & Naming Conventions

Use four-space indentation and standard C# conventions: PascalCase for public types, methods, and properties; camelCase for local variables and parameters; `I` prefixes for interfaces. Keep nullable warnings meaningful: do not suppress nullability diagnostics without a reason. Use `async`/`await` for I/O, accept and propagate `CancellationToken` where an operation can be cancelled, and avoid exposing infrastructure types from domain or application contracts.

Place new code in the appropriate layer and mirror existing names, for example `Application/Abstractions/IHttpRequestSender.cs` and `Infrastructure/Http/HttpClientRequestSender.cs`. Avoid bypassing the adapters from `ClientExtensión/` with direct HTTP calls.

## Security & Configuration

Never commit access tokens, app passwords, OAuth client secrets, or local user-secret files. Use .NET user secrets or environment variables for local credentials. Credential storage belongs in the relevant infrastructure layer; preserve the existing protected-data approach for persisted secrets.

## Commits & Pull Requests

The repository has no commit history, so no commit-message convention is established. Use concise imperative subjects, such as `Add Jira issue lookup`. Keep commits scoped to one behavior. Pull requests should describe the affected adapter or extension workflow, explain configuration changes, link the relevant work item, and include screenshots for Visual Studio UI changes. State the build or manual verification performed.
