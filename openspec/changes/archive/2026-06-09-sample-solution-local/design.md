## Context

`extract-web-framework-package` delivers `Tjb.Web.Framework` (RCL), `Tjb.Web.Hosting` (extensions), and an Identity-only base `DbContext`, published to GitHub Packages. This change builds the first *external* consumer of them: a sample web app under `src/sample`. The point is to fail fast on any packaging gap a project-reference build would hide, and to give `sample-ci-deploy` a buildable artifact.

The existing `Tjb.*` projects are the structural template: `Tjb.Web` (host) + `Tjb.Data` (context/entities) + `Tjb.Migrations` (migrations + design-time factory + migrate/seed runner) + `Tjb.Api` (health/Swagger). The sample mirrors that shape with `Sample.*` so the eventual deploy story matches what the reusable workflow expects (`web-project-path`, Dockerfile location, migrations runner).

## Goals / Non-Goals

**Goals:**
- A `src/sample` solution that consumes the framework via NuGet only and runs locally end-to-end (login from the package, authenticated CRUD over the sample's own data, migrations apply).
- Prove decoupling: zero `ProjectReference` into `src/Tjb.*`; a standalone `Sample.sln`.
- Establish the consumer `DbContext` pattern (derive the framework Identity base, add own entities) concretely.
- Keep the sample minimal but structurally complete enough that `sample-ci-deploy` can lift it into CI with no surprises.

**Non-Goals:**
- Any CI, deploy, or CloudFormation work (that is `sample-ci-deploy`).
- A rich domain — Guestbook/Notes is deliberately trivial.
- Re-validating the framework internals (that is `extract-web-framework-package`'s job); here we treat the packages as a black box and only validate consumption.

## Decisions

### D1. Separate `Sample.sln`, separate folder, NuGet-only references

`src/sample` gets its own `Sample.sln`. The framework + `Tjb.Shared` come in via `PackageReference` pinned to the version `extract-web-framework-package` published. A `nuget.config` under `src/sample` (or documented `dotnet nuget add source`) points at the GitHub Packages feed. **No `ProjectReference` may resolve into `src/Tjb.*`** — this is the decoupling invariant and is assert-checked in tasks (grep the csproj files).

**Why a separate solution:** a consumer in the real world is a different repo; the closest in-repo approximation is a self-contained solution that never leans on `Tjb.sln`'s project graph. It also keeps `Tjb.sln` build/test times unaffected.

**Alternative considered — add `Sample.*` to `Tjb.sln`:** convenient but lets a stray `ProjectReference` compile, silently defeating the decoupling test. Rejected.

### D2. Domain: Guestbook/Notes, one or two entities

`Sample.Data` defines a single `GuestbookEntry` (Id, AuthorUserId, Message, CreatedUtc) — optionally a second related entity if a one-to-many is wanted later. The authenticated page lists entries and creates a new one tied to the signed-in user. This exercises: a consumer `DbContext` deriving the framework Identity base, a consumer migration, and an authenticated read/write path through framework Identity into consumer data — the full reuse seam — with near-zero domain noise.

### D3. `SampleDbContext` derives the framework Identity base

`class SampleDbContext : AwsWebAppIdentityDbContext { public DbSet<GuestbookEntry> GuestbookEntries {get;set;} protected override void OnModelCreating(ModelBuilder b){ base.OnModelCreating(b); /* GuestbookEntry config */ } }`. `Sample.Migrations` is a **class library** holding the EF migrations + an `IDesignTimeDbContextFactory<SampleDbContext>` (reading its own `appsettings.json`); the context registration wires `MigrationsAssembly("Sample.Migrations")`. Migrations are generated with `--project src/sample/Sample.Migrations --startup-project src/sample/Sample.Migrations`, and **applied** via `dotnet ef database update` or `Sample.Web`'s startup migration (`ApplyDatabaseMigrationsAsync<SampleDbContext>`).

> **Correction (post-implementation):** the original D3 said `Sample.Migrations` mirrors `Tjb.Migrations` with "a standalone `Program` that migrates + seeds." That conflates a migrations *assembly* (a library the app references and `dotnet ef` drives) with a runnable tool — and it isn't how migration assemblies are used. The standalone `Program` (and the `OutputType=Exe` it forced) was dropped: the sample has no seed, and `Sample.Web` already migrates on startup, so the runner was redundant. `Sample.Migrations` is a plain library. (`Tjb.Migrations` carries the same conflation — its documented standalone `dotnet run` never actually worked; a separate cleanup.)

If `extract-web-framework-package` resolves its Open Question 1 toward a generic `AwsWebAppIdentityDbContext<TUser>`, the sample derives the concrete `IdentityUser` form; the sample is a good forcing function for that decision and any rough edges should be reported back to that change.

### D4. `Sample.Web` host shape mirrors `Tjb.Web`

`Sample.Web` is `Microsoft.NET.Sdk.Web`, `net10.0`, Blazor Server. Its `Program.cs` is the thin-host shape the framework enables: register `SampleDbContext`, call the four `Add*` extensions, add `AddRazorPages`/`AddServerSideBlazor`/`AddHealthChecks`, then `UseAwsWebAppForwardedHeaders` + standard pipeline + `MapFallbackToPage("/_Host")` + `ApplyDatabaseMigrationsAsync<SampleDbContext>`. It carries a `Dockerfile` modeled on `src/Tjb.Web/Dockerfile` (same `aspnet:10.0` base) so `sample-ci-deploy` can build an image at a predictable path. `appsettings.json` uses localhost MySQL defaults; real config comes from env/secrets in the deploy change.

### D5. `Sample.Api` minimal, included for shape parity

A minimal `Sample.Api` (`/health` + Swagger) is included so the consumer's project set matches the `Tjb.*` topology the reusable deploy workflow assumes. It is intentionally inert beyond health, matching `Tjb.Api`'s vestigial status. It can be dropped if `sample-ci-deploy` proves it unnecessary.

## Risks / Trade-offs

- **[Risk] A packaging gap in the framework (missing static assets, wrong content paths, internal-only types) only surfaces here.** → That is the *point*; this change is where such gaps are meant to be caught cheaply, before deploy. File any gap back to `extract-web-framework-package` (it may need a follow-up patch release).
- **[Risk] GitHub Packages auth friction locally (PAT scoped to `read:packages`).** → Document the `nuget.config` + PAT setup in the sample's README; it mirrors how the org already consumes `Tjb.Shared`.
- **[Risk] Google OAuth requires real client credentials even locally.** → For the local checkpoint, allow a documented path that exercises Identity (email/password or the framework login UI) without completing the full Google round-trip; full OAuth is validated against a deployed URL in `sample-ci-deploy`. Confirm what the framework requires at minimum to boot.
- **[Trade-off] Maintaining a second solution + Dockerfile in-repo.** → Acceptable and intended: it is the living proof the framework is reusable and the substrate for the deploy change.

## Open Questions

1. **Sample subdomain/name → RESOLVED: `sample`.** ProjectName/leaf `sample`; .NET projects `Sample.*` under `src/sample`; deploy URL `sample.appcloud.systems`, image `sample-web` (locked for `sample-ci-deploy`). Not `Tjb.*` — the consumer must look like a separate product to keep the decoupling story honest.
2. **`Sample.Shared` vs consume `Tjb.Shared`? → RESOLVED: add a dedicated `Sample.Shared`** (chose the clean-API-boundary option over the consume-`Tjb.Shared` default). The sample carries its own shared contract; the "consumes from the feed" proof comes from the framework packages (`Tjb.Web.Framework*`/`Hosting`), not `Tjb.Shared`. See task 3.0.
3. **Minimum config to boot `Sample.Web` locally → RESOLVED: full Google OAuth locally.** Real Google ClientId/Secret via user-secrets + a localhost callback (not the email/password shortcut). Min local config = MySQL `DefaultConnection` + `Authentication:Google:ClientId`/`ClientSecret`. Note `AddAwsWebAppIdentity` sets `RequireConfirmedAccount = true`, so first sign-in hits the SES confirmation path — supply an `AwsSes` sender/region locally or accept the confirmation email won't send. Because this needs an operator-provisioned Google app + running MySQL, the run-checkpoints (3.5/6.2/6.3) are validated by the operator, not in this scaffolding pass.
