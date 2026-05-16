# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Stack reality vs. README

The root `README.md` is partially out of date — trust the code over the README:

- **Framework**: `net10.0` (not net8). All projects target it. CI uses `dotnet-version: 10.0.x`.
- **Database**: MySQL via `Pomelo.EntityFrameworkCore.MySql` (not PostgreSQL). Production runs Aurora MySQL Serverless v2 (Global Cluster on `app`/`beta`/`alpha`). README mentions PostgreSQL/RDS — ignore.
- **Hosting**: Containerized — Dockerfile at [src/TaskManager.Web/Dockerfile](src/TaskManager.Web/Dockerfile) builds a `mcr.microsoft.com/dotnet/aspnet:10.0` image pushed to ECR and run behind an ALB. The README's "AWS Lambda + API Gateway" description is stale; only `TaskManager.Api` retains Lambda packaging code (`Amazon.Lambda.AspNetCoreServer`) but the deployed surface is the Web container. Treat `TaskManager.Api` as a vestigial/secondary project — `TaskManager.Web` is the live application.
- **Auth**: All real authentication lives in `TaskManager.Web` (ASP.NET Identity + Google OAuth). [src/TaskManager.Api/Controllers/AuthController.cs](src/TaskManager.Api/Controllers/AuthController.cs) is intentionally a no-op ("authentication disabled") — don't try to "fix" it.

## Common commands

```powershell
# Build / test (run from repo root)
dotnet restore
dotnet build --configuration Release
dotnet test --filter "FullyQualifiedName!~TaskManager.UiTests"   # CI runs unit tests this way; UI tests are excluded pre-deploy

# Run the web app locally (Blazor Server + Identity + Google OAuth)
dotnet run --project src/TaskManager.Web

# Run the API locally (mostly health endpoint + Swagger)
dotnet run --project src/TaskManager.Api

# EF Core migrations — migrations live in their OWN project, not in Data
dotnet ef migrations add <Name> --project src/TaskManager.Migrations --startup-project src/TaskManager.Migrations
dotnet ef database update           --project src/TaskManager.Migrations --startup-project src/TaskManager.Migrations

# Apply migrations + seed (standalone runner)
dotnet run --project src/TaskManager.Migrations

# UI tests (Playwright/xUnit) — point at a deployed env via TEST_BASE_URL
cd src/TaskManager.UiTests
dotnet build                                # also restores Playwright browsers
dotnet test                                 # runs against BaseUrl in appsettings.json (default: https://dev.appcloud.systems)
dotnet test --filter "FullyQualifiedName~LoginNavigation"   # single test

# Connect to the deployed Aurora DB through the bastion
.\Connect-TaskManagerDB.ps1 -UseSSM
```

## Architecture

Six projects in [TaskManager.sln](TaskManager.sln):

- **TaskManager.Shared** — DTOs and enums (`TaskStatus`, `TaskPriority`, `ProjectRole`). Packed as a NuGet on every CI build.
- **TaskManager.Data** — EF Core `DbContext`, entity classes, configurations. `TaskManagerDbContext` extends `IdentityDbContext<IdentityUser>`, so Identity tables share the same DB. The DbContext is wired via `mysqlOptions.MigrationsAssembly("TaskManager.Migrations")` — migrations are NOT generated into this project.
- **TaskManager.Migrations** — Holds EF migration files, an `IDesignTimeDbContextFactory` (so `dotnet ef` can resolve a connection string from its own `appsettings.json`), and a standalone `Program.cs` that applies migrations + seeds initial data. This is also referenced by `Api` and `Web` so they can apply migrations on startup.
- **TaskManager.Api** — Minimal Web API. Currently exposes only `/health`, Swagger (in dev), and stub `AuthController` endpoints. Still contains Lambda hosting glue (`LambdaEntryPoint`, `Startup`) but is not the deployed front door.
- **TaskManager.Web** — **The deployed application.** Blazor Server + Razor Pages + ASP.NET Identity + Google OAuth. On startup it calls `EnsureCreatedAsync()` then `MigrateAsync()`. Sits behind an ALB so it configures `ForwardedHeaders` (`X-Forwarded-Proto`/`-For`) with `KnownProxies`/`KnownNetworks` cleared — needed for the Google OAuth `/signin-google` callback to see HTTPS.
- **TaskManager.UiTests** — Playwright + xUnit. Runs against a *deployed* URL, not a local server. Uses token-based Google auth in CI (`GOOGLE_TEST_ACCESS_TOKEN`/`REFRESH_TOKEN`) rather than scripting the OAuth UI.

Both `Web` and `Api` apply migrations on startup but **swallow exceptions** so the app still boots if migrations fail (intentional, to avoid Lambda/cold-start crashes). Don't change this to throw without thinking through the deployment story.

## Branch model & CI/CD ([BRANCH_MANAGEMENT_README.md](BRANCH_MANAGEMENT_README.md), [.github/workflows/zbuild.yml](.github/workflows/zbuild.yml))

This repo has an unusual branching scheme — read carefully before doing anything git-related:

- **PRs target `app`** (the production branch). `app` is also the GitVersion `main`.
- Three "shared infrastructure" branches deploy multi-region (us-east-1 + us-west-2): `app`, `beta`, `alpha`. They use `infrastructure/master.template` (full backend incl. Aurora Global Cluster).
- `dev` is single-region but also uses the master template.
- Every other branch follows `{type}/{name}` where type is `build|deploy|system|feature|fix`. These deploy `infrastructure/application.template` (just the app stack, importing backend exports from `dev`) into a per-branch CloudFormation stack named `{branch-leaf}-{processed-domain}`.
- Every push triggers `Deploy Everything` workflow → builds, tests, builds Docker image, deploys via SAM/CloudFormation, runs UI tests against the deployed URL `https://{branch-leaf}.{DOMAIN_NAME}`, then publishes NuGets to GitHub Packages.

Useful workflow controls:
- Add `nodeploy` to a non-merge commit message to skip the deploy + UI test + publish jobs.
- The `dev`/`alpha`/`beta`/`app` branches **fail the build if `changes/` is non-empty** — use `scripts/merge-to-dev.ps1` (or the `.sh` variant) to flush pending change files into `changelog.md`.
- New branches should be created with `scripts/create-branch.ps1`, which generates a `changes.md` whose first non-empty line becomes the changelog entry on merge into `dev`.
- The Docker image is content-addressed by a SHA256 of `src/`; if an image with that tag already exists in ECR, the build step is skipped.

## Things that will trip you up

- **Don't add migrations to `TaskManager.Data`** — the `MigrationsAssembly` is `TaskManager.Migrations`. EF tooling needs `--project src/TaskManager.Migrations`.
- **`TaskManager.Api` is mostly inert.** Adding endpoints there won't reach users; add them to `TaskManager.Web`.
- **Connection strings in `appsettings.json` are localhost defaults** (`Server=localhost;...root/password`). Real values come from environment / Secrets Manager in deployed envs and from `dotnet user-secrets` locally.
- **Forwarded headers config in [src/TaskManager.Web/Program.cs](src/TaskManager.Web/Program.cs) clears `KnownProxies`/`KnownNetworks` on purpose** — the ALB has dynamic IPs. Don't tighten it without verifying OAuth still works.
- **NuGet package versions are branch-suffixed** for non-`app` branches (`{version}-{sanitized-branch}`) so consumers can pin to a feature branch's build.
