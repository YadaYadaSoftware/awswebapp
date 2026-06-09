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
| `Sample.Migrations` | EF migrations + design-time factory (a class library; not runnable) | `Tjb.Migrations` |
| `Sample.Web` | Blazor Server thin host wired via the framework `Add*`/`Use*` extensions | `Tjb.Web` |
| `Sample.Api` | minimal `/health` + Swagger (shape parity) | `Tjb.Api` |

The framework is pinned to **`1.1.0.190-dev`** (the version published to GitHub Packages by the
`extract-web-framework-package` change on `dev`).

## Prerequisites

1. **.NET 10 SDK.**
2. **A GitHub PAT with `read:packages`** to restore the framework packages from this org's feed.

   **Create it:** <https://github.com/settings/tokens/new?scopes=read:packages&description=sample-read-packages>
   (the link pre-selects the `read:packages` scope; pick a short expiry, and if the `YadaYadaSoftware`
   org enforces SSO, click **Configure SSO → Authorize** on the token). Copy the `ghp_…` value.

   **Where it goes:** the token is **not** pasted into any file — [`nuget.config`](nuget.config)
   references it as `%GH_PACKAGES_TOKEN%`, so you put the value in an **environment variable named
   `GH_PACKAGES_TOKEN`**. NuGet expands it at `dotnet restore` time. Never commit the literal token.

   **How to set it** — option A, **current shell only** (simplest; gone when the shell closes):
   ```powershell
   # PowerShell
   $env:GH_PACKAGES_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
   ```
   ```bash
   # bash/zsh
   export GH_PACKAGES_TOKEN="ghp_xxxxxxxxxxxxxxxxxxxx"
   ```
   Option B, **persist for future shells** (a new terminal picks it up automatically):
   ```powershell
   # PowerShell (Windows, user-scoped) — restart the terminal afterward
   [Environment]::SetEnvironmentVariable('GH_PACKAGES_TOKEN', 'ghp_xxxxxxxxxxxxxxxxxxxx', 'User')
   ```
   ```bash
   # bash/zsh — add to ~/.bashrc or ~/.zshrc, then open a new shell
   echo 'export GH_PACKAGES_TOKEN="ghp_xxxxxxxxxxxxxxxxxxxx"' >> ~/.bashrc
   ```
   Run `dotnet restore`/`build` from the **same shell** that has `GH_PACKAGES_TOKEN` set. Revoke the
   token at <https://github.com/settings/tokens> when you're done.
3. **A local MySQL** (8.0) reachable at the `DefaultConnection` connection string.
4. **HTTPS dev certificate** — the app runs on HTTPS (`https://localhost:7242`, see launch profile);
   trust the dev cert once: `dotnet dev-certs https --trust`.
5. **A Google OAuth app** *(optional locally)* — local auth is the full Google round-trip (Q3). Create
   a client id/secret with an authorized redirect URI of **`https://localhost:7242/signin-google`**
   and supply them via user-secrets (below). **Google is optional**: if `Authentication:Google:ClientId`
   is empty the app still runs (the Google button is just absent) — set the creds to enable it. The
   framework sets `RequireConfirmedAccount = true`, so first sign-in triggers the SES confirmation-email
   path — set an `AwsSes` sender/region locally, or accept that the confirmation email won't send.

## Local run

```powershell
# run these from this folder (src/sample/), with GH_PACKAGES_TOKEN exported
dotnet restore Sample.sln
dotnet build   Sample.sln -c Release      # Tjb.sln is NOT loaded

# required local config (user-secrets on Sample.Web)
dotnet user-secrets --project Sample.Web set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=sampledb;User=root;Password=password;"

# OPTIONAL — enable Google sign-in (omit to run without it)
dotnet user-secrets --project Sample.Web set "Authentication:Google:ClientId" "<google-client-id>"
dotnet user-secrets --project Sample.Web set "Authentication:Google:ClientSecret" "<google-client-secret>"

# apply migrations — via the design-time factory (needs the dotnet-ef tool: dotnet tool install -g dotnet-ef)
dotnet ef database update --project Sample.Migrations --startup-project Sample.Migrations
# (or skip the line above and just run Sample.Web — it migrates on startup)
dotnet run --project Sample.Web         # serves https://localhost:7242
```

Then browse to **https://localhost:7242**, sign in (Google if configured), and use the Guestbook
page to create/list entries.
