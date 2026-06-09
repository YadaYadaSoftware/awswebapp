## 1. Prereqs

- [x] 1.1 Confirm `extract-web-framework-package` has published the framework packages; record the version. — *CONFIRMED on GitHub Packages at **`1.1.0.190-dev`**: `Tjb.Web.Framework`, `Tjb.Web.Hosting`, `Tjb.Web.Framework.Data` (the Identity-only base `AwsWebAppIdentityDbContext`), pushed by dev run `27217107673`. The sample pins `1.1.0.190-dev`.*
- [x] 1.2 Branch from `dev` with the spec's exact name. — *Done: `sample-solution-local` off dev tip `2884a82` (wilhelm worktree).*
- [x] 1.3 Resolve design Open Questions (Q1/Q2/Q3). — *DECIDED: **Q1** ProjectName/leaf = `sample` (projects `Sample.*` under `src/sample`; deploy URL `sample.appcloud.systems`, image `sample-web`). **Q2 = add a dedicated `Sample.Shared`** (deviates from the design's consume-`Tjb.Shared` default — see new task 3.0). **Q3 = real Google OAuth locally** (full round-trip, not email/password) — requires a Google OAuth app + ClientId/Secret via user-secrets and a localhost callback; the run-checkpoints (3.5/6.2/6.3) are therefore gated on the operator's local env (MySQL + Google app). Framework note: `AddAwsWebAppIdentity` sets `RequireConfirmedAccount = true`, so first sign-in triggers the SES confirmation path — local config must supply an `AwsSes` sender/region or accept that the confirmation email won't send locally.*

## 2. Solution + feed scaffolding

- [x] 2.1 Create `src/sample/Sample.sln` + `src/sample/nuget.config`. — *Done: classic-format `Sample.sln`; `nuget.config` mirrors the repo's feed-auth (`%GH_PACKAGES_TOKEN%`, `Tjb.*` mapped to the GitHub Packages feed, everything else nuget.org), committed under `src/sample` so the sample is self-contained.*
- [x] 2.2 Add `src/sample/README.md` (feed/PAT setup + local run steps). — *Done: documents the `read:packages` PAT as `GH_PACKAGES_TOKEN`, local MySQL, the Google OAuth app (Q3), user-secrets, and the restore/build/migrate/run sequence. Records the pinned framework version `1.1.0.190-dev` and the project layout.*

## 3. Sample.Shared + Sample.Data + Sample.Migrations

- [x] 3.0 (Q2) Create `src/sample/Sample.Shared`. — *Done: `Sample.Shared.csproj` (net10.0) + `GuestbookEntryDto`. Sample's own shared contract; no `Tjb.*` ref.*
- [x] 3.1 Create `src/sample/Sample.Data`. — *Done: `PackageReference Tjb.Web.Framework.Data 1.1.0.190-dev` (restored from GitHub Packages ✓) + `Pomelo 8.0.2` + `EFCore.Design 8.0.11`; `ProjectReference` to `Sample.Shared`. No `Tjb.*` project ref, no `Tjb.Shared` package (§6.4 verified). Builds clean.*
- [x] 3.2 Define the Guestbook domain. — *Done: `GuestbookEntry` (Id/AuthorUserId/Message/CreatedUtc) + `SampleDbContext : AwsWebAppIdentityDbContext` with `DbSet<GuestbookEntry>`, `OnModelCreating` calls `base` first then configures the entry; `OnConfiguring` sets `MigrationsAssembly("Sample.Migrations")`.*
- [x] 3.3 Create `src/sample/Sample.Migrations`. — *Done: `SampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>` (FIXED `MySqlServerVersion 8.0.35` so migrations generate without a live DB), `appsettings.json`, and a standalone migrate `Program` (no seed — Guestbook entries are user-created).*
- [x] 3.4 Generate the initial migration. — *Done: `dotnet ef migrations add InitialCreate` (EF tools 8.0.27) produced `20260609174217_InitialCreate` + snapshot. Verified the migration creates the full Identity schema (`AspNetUsers`/`Roles`/…) from the framework base **and** `GuestbookEntries` — the reuse seam works at the model level.*
- [ ] 3.5 **Checkpoint (operator):** run `dotnet run --project src/sample/Sample.Migrations` against a fresh local MySQL; confirm Identity + `GuestbookEntries` tables are created. — *GATED on a local MySQL (not available in this scaffolding session). The migration is generated and build-verified; applying it needs the operator's DB.*

## 4. Sample.Web host

- [ ] 4.1 Create `src/sample/Sample.Web/Sample.Web.csproj` (`Microsoft.NET.Sdk.Web`, `net10.0`); `PackageReference` to `Tjb.Web.Framework` + `Tjb.Web.Hosting` + `Tjb.Shared`; `ProjectReference` to `Sample.Data` + `Sample.Migrations` (sample-internal only). Verify no `src/Tjb.*` project references.
- [ ] 4.2 Write `Program.cs` as the thin host: register `SampleDbContext`, call `AddAwsWebAppIdentity<SampleDbContext>`, `AddAwsWebAppGoogleAuth`, `AddAwsWebAppEmail`, host `AddRazorPages`/`AddServerSideBlazor`/`AddHealthChecks`, then `UseAwsWebAppForwardedHeaders` + pipeline + `MapFallbackToPage("/_Host")` + `await app.ApplyDatabaseMigrationsAsync<SampleDbContext>()`.
- [ ] 4.3 Add `App.razor`/`_Imports.razor`/`appsettings*.json` and a home page; rely on the framework RCL for layout, Identity area, and static assets (none copied locally).
- [ ] 4.4 Add the authenticated Guestbook page: lists entries and creates a new one tied to the signed-in user via `SampleDbContext`.
- [ ] 4.5 Add a `Dockerfile` modeled on `src/Tjb.Web/Dockerfile` (base `aspnet:10.0`), build context resolving `Sample.Web`.
- [ ] 4.6 Document and set the minimum local boot config (user-secrets: connection string, and whatever the framework requires for SES/Google to not fault at startup).

## 5. Sample.Api (minimal)

- [ ] 5.1 Create `src/sample/Sample.Api` mirroring `Tjb.Api`: `/health` + Swagger in dev. Keep it minimal; no auth surface.

## 6. Local validation

- [ ] 6.1 **Checkpoint:** `dotnet build src/sample/Sample.sln` succeeds with `Tjb.sln` NOT loaded (restoring the framework from the feed).
- [ ] 6.2 **Checkpoint:** `dotnet run --project src/sample/Sample.Web`; `/Identity/Account/Login` renders the framework login page (served from the package); shared layout/CSS intact.
- [ ] 6.3 **Checkpoint:** sign in (per the documented local auth path) and exercise the Guestbook page — create an entry, confirm it persists via `SampleDbContext` and lists back.
- [ ] 6.4 Assert decoupling: grep all `src/sample/**/*.csproj` for `ProjectReference` paths into `src/Tjb.*` — must be zero.
- [ ] 6.5 (Optional) Add a `Sample.UiTests` Playwright smoke for local run; full UI-test wiring is deferred to `sample-ci-deploy`.

## 7. Docs, validation, handoff

- [ ] 7.1 Update `src/sample/README.md` and reference the sample from `CLAUDE.md`/root `README.md` as the framework's reference consumer.
- [ ] 7.2 File any framework packaging gaps discovered here back to `extract-web-framework-package` (may need a patch release).
- [ ] 7.3 `openspec validate sample-solution-local --strict` and resolve issues.
- [ ] 7.4 Record the sample's `ProjectName`/subdomain and project paths for `sample-ci-deploy` to consume.
- [ ] 7.5 Archive this change (`/opsx:archive`) once merged to `dev` and validated.
