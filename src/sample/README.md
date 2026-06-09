# Sample — reference consumer of the AWS Web App framework

`src/sample` is a **standalone .NET 10 web app** that consumes the framework packages
(`Tjb.Web.Framework`, `Tjb.Web.Hosting`, `Tjb.Web.Framework.Data`) **only via NuGet** — it has
**zero `ProjectReference` into `src/Tjb.*`**. It exists to prove the framework is reusable by a
*different* application and to give [`sample-ci-deploy`](../../openspec/changes/sample-ci-deploy/)
a buildable artifact. The trivial domain is a **Guestbook** (authenticated users post entries).

> Built by the `sample-solution-local` OpenSpec change. Decisions: ProjectName/leaf `sample`
> (deploys to `sample.appcloud.systems` later); its own `Sample.Shared`; full Google OAuth locally.

## Project layout

| Project | Role | Mirrors |
| --- | --- | --- |
| `Sample.Shared` | DTOs/enums for the sample | `Tjb.Shared` |
| `Sample.Data` | `SampleDbContext : AwsWebAppIdentityDbContext` + `GuestbookEntry` | `Tjb.Data` |
| `Sample.Migrations` | EF migrations + design-time factory + migrate/seed runner | `Tjb.Migrations` |
| `Sample.Web` | Blazor Server thin host wired via the framework `Add*`/`Use*` extensions | `Tjb.Web` |
| `Sample.Api` | minimal `/health` + Swagger (shape parity) | `Tjb.Api` |

The framework is pinned to **`1.1.0.190-dev`** (the version published to GitHub Packages by the
`extract-web-framework-package` change on `dev`).

## Prerequisites

1. **.NET 10 SDK.**
2. **A GitHub PAT with `read:packages`** to restore the framework packages from this org's feed.
   Export it as `GH_PACKAGES_TOKEN` (consumed by [`nuget.config`](nuget.config)):
   ```powershell
   $env:GH_PACKAGES_TOKEN = "<your PAT with read:packages>"
   ```
   ```bash
   export GH_PACKAGES_TOKEN="<your PAT with read:packages>"
   ```
3. **A local MySQL** (8.0) reachable at the `DefaultConnection` connection string.
4. **A Google OAuth app** (local auth is the full Google round-trip — Q3): a client id/secret with
   an authorized redirect URI of `https://localhost:<port>/signin-google`. Supply them via
   user-secrets (below). Note the framework sets `RequireConfirmedAccount = true`, so first sign-in
   triggers the SES confirmation-email path — set an `AwsSes` sender/region locally, or accept that
   the confirmation email won't send in local dev.

## Local run

```powershell
# from repo root, with GH_PACKAGES_TOKEN exported
dotnet restore src/sample/Sample.sln
dotnet build   src/sample/Sample.sln -c Release      # Tjb.sln is NOT loaded

# minimum local config (user-secrets on Sample.Web)
dotnet user-secrets --project src/sample/Sample.Web set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=sampledb;User=root;Password=password;"
dotnet user-secrets --project src/sample/Sample.Web set "Authentication:Google:ClientId" "<google-client-id>"
dotnet user-secrets --project src/sample/Sample.Web set "Authentication:Google:ClientSecret" "<google-client-secret>"

# apply migrations + seed, then run
dotnet run --project src/sample/Sample.Migrations
dotnet run --project src/sample/Sample.Web
```

Then browse to the app, sign in via Google, and use the Guestbook page to create/list entries.
