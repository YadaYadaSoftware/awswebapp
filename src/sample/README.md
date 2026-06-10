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

## Deploy via the GitHub pipeline (CI)

The local run above needs no AWS. To deploy the sample to AWS it ships a GitHub Actions pipeline —
[`.github/workflows/sample-deploy.yml`](../../.github/workflows/sample-deploy.yml), a thin caller of
the reusable [`deploy.yml`](../../.github/workflows/deploy.yml):

- **Trigger:** a push touching `src/sample/**` runs the workflow. It builds/tests `Sample.sln`
  (restoring the framework packages), deploys via CloudFormation, then runs the UI tests against
  `https://{branch-leaf}.sample.appcloud.systems`.
- **Gated off by default:** the pipeline runs only when the repo Variable `SAMPLE_DEPLOY_ENABLED` is
  `true`, so it stays dormant in TaskManager's CI. Set it once the AWS prerequisites are in place.
- **A `validate-config` preflight runs first** and fails fast with a checklist naming any required
  repo Variable/Secret that's unset — so missing config surfaces immediately, not deep in a Docker
  build.

**What to configure** (Settings → Secrets and variables → Actions):

| Variables (required) | Secrets (required) | Optional |
| --- | --- | --- |
| `DOMAIN_NAME` | `AWS_ACCESS_KEY_ID` | `AWS_ACCESS_KEY_ID_PROD` |
| `AWS_REGION_PRIMARY` | `AWS_SECRET_ACCESS_KEY` | `AWS_SECRET_ACCESS_KEY_PROD` |
| `AWS_REGION_SECONDARY` | `DATABASE_PASSWORD` | `GOOGLE_TEST_ACCESS_TOKEN` |
| `HOSTED_ZONE_ID` | `GOOGLE_CLIENT_ID` | `GOOGLE_TEST_REFRESH_TOKEN` |
| `SAMPLE_DEPLOY_ENABLED` = `true` | `GOOGLE_CLIENT_SECRET` | |
| | `FRAMEWORK_FEED_TOKEN` | |

This table mirrors the preflight's lists exactly. `FRAMEWORK_FEED_TOKEN` is a cross-org
`read:packages` PAT used to restore the framework packages; it **falls back to `GITHUB_TOKEN`** when
unset, so a same-org caller (TaskManager) needs nothing new.

**Before enabling**, complete the AWS prerequisites (bootstrap stack, Route 53 hosted zone + ACM, a
`dev` backend, optional SES) in **[DEPLOYING.md](DEPLOYING.md)** — the operator runbook for this
pipeline. If you're **forking this repo** to stand up your own app, follow the root README's
**[Setting up a new repo](../../README.md#setting-up-a-new-repo)** guide instead, which wraps these
same steps with the fork → keep-vs-delete → promote path.

## Notes for framework consumers (gotchas)

Things any app consuming `Tjb.Web.Framework` (not just this sample) needs to know:

- **Reference `Microsoft.AspNetCore.Identity.UI` directly.** The framework RCL brings the Identity UI
  pages, but the consuming web app must add its own
  `<PackageReference Include="Microsoft.AspNetCore.Identity.UI" Version="8.0.10" />`. The Identity UI's
  default pages (e.g. `/Identity/Account/Login`) link their CSS from that package's static web assets
  under `~/Identity/...`; those assets are **not** served through the transitive (via-RCL) reference,
  so the Login/Register pages render **unstyled** without the direct reference. (The Blazor pages are
  unaffected — they pull CSS from the RCL's own `_content/Tjb.Web.Framework/...` assets, which *do*
  flow transitively.) See `Sample.Web.csproj`.
- **Register Google OAuth only when configured.** `AddAwsWebAppGoogleAuth` wires `AddGoogle`, whose
  options validate a non-empty `ClientId` on every request — so registering it without
  `Authentication:Google:ClientId` set throws on each request. Guard the call (see `Program.cs`) if you
  want the app to run locally without Google creds.
- **`RequireConfirmedAccount = true`.** The framework requires email confirmation on first sign-in, so a
  consumer needs a working `AwsSes` sender/region (or must accept the confirmation email won't send in
  local dev).
