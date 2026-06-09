## Why

Once `extract-web-framework-package` ships the reusable web framework as NuGet packages, we need proof that a *different* .NET web app can actually be built on it — not by copying `Tjb.*` source, but by consuming the packages. This change scaffolds that proof: a sample solution under `src/sample` that reuses the framework end-to-end and runs locally. It is the decoupling test (NuGet-only, zero project references into `src/Tjb.*`) and the foundation the CI-deploy change builds on. Without a working sample, "spin up more than one web app from the framework" stays theoretical.

Second of the coordinated set: `extract-web-framework-package` → **`sample-solution-local`** (this) → `sample-ci-deploy` → `framework-slipstream-upgrade`.

## What Changes

- **New solution area `src/sample`** with its own solution file (`Sample.sln`) so the sample builds independently of `Tjb.sln` — reinforcing decoupling.
- **`Sample.Web`** — a thin Blazor Server host that references `Tjb.Web.Framework` + `Tjb.Web.Hosting` (and `Tjb.Shared`) **via `PackageReference` only**. Its `Program.cs` registers `SampleDbContext`, calls the framework extensions (`AddAwsWebAppIdentity`, `AddAwsWebAppGoogleAuth`, `AddAwsWebAppEmail`, `UseAwsWebAppForwardedHeaders`, `ApplyDatabaseMigrationsAsync`), and maps its own pages. Carries its own `Dockerfile` modeled on `Tjb.Web`'s.
- **`Sample.Data`** — `SampleDbContext` deriving the framework's Identity-only base context and adding the sample's own entities. Domain: a minimal **Guestbook/Notes** capability (one or two entities) — enough to exercise a consumer-defined `DbContext`, migration, and authenticated CRUD page without copying TaskManager's task entities.
- **`Sample.Migrations`** — EF migrations project for `SampleDbContext` (its own `IDesignTimeDbContextFactory` + standalone migrate/seed `Program`), mirroring the `Tjb.Migrations` pattern.
- **`Sample.Api`** (optional/minimal) — a `/health` + Swagger API mirroring `Tjb.Api`, included to prove the multi-project consumer shape; kept minimal.
- **Sample authenticated page** — at least one Blazor page behind Identity that lists/creates Guestbook entries via `SampleDbContext`, proving framework auth + consumer data compose.
- **Local-only scope** — this change does not touch CI, the deploy workflow, or CloudFormation. Build, run, and test happen on a developer machine against local MySQL.

## Capabilities

### New Capabilities

- `sample-consumer-solution`: A reference consumer solution (`src/sample`) that reuses the web framework purely through NuGet packages, defines its own data model on the framework's Identity base, and runs/tests locally. Covers: the project layout (`Sample.Web`/`Sample.Data`/`Sample.Migrations`/`Sample.Api`); the NuGet-only consumption constraint (no `src/Tjb.*` project references); the consumer `DbContext` deriving the framework Identity base; the local build/run/test checkpoints (login renders from the package, authenticated sample page does CRUD, migrations apply); and the sample's independence from `Tjb.sln`.

### Modified Capabilities

_None._ This change adds a new consumer; it does not change any existing capability's requirements. It depends on the `web-framework-package` capability delivered by `extract-web-framework-package`.

## Impact

**New artifacts:**
- `src/sample/Sample.sln`
- `src/sample/Sample.Web/` (host, `Dockerfile`, `appsettings*.json`, at least one authenticated Guestbook page)
- `src/sample/Sample.Data/` (`SampleDbContext` + Guestbook entity/entities)
- `src/sample/Sample.Migrations/` (migrations + design-time factory + migrate/seed runner)
- `src/sample/Sample.Api/` (minimal health/Swagger)
- `src/sample/Sample.UiTests/` (optional local Playwright smoke; full UI-test wiring is `sample-ci-deploy`)
- A `nuget.config` (or documented feed step) so `dotnet restore` resolves the framework + `Tjb.Shared` packages from GitHub Packages.

**Depends on:**
- `extract-web-framework-package` — the `Tjb.Web.Framework` / `Tjb.Web.Hosting` packages and the Identity-only base context must be published to GitHub Packages first.

**Explicitly out of scope:**
- CI build/test, the deploy workflow, and CloudFormation — that is `sample-ci-deploy`.
- Multi-region or any AWS-deployed surface.
- Proving the bug-fix slipstream loop — that is `framework-slipstream-upgrade`.
