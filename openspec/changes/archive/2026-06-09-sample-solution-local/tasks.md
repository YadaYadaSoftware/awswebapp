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

- [x] 4.1 Create `Sample.Web.csproj`. — *Done (`Microsoft.NET.Sdk.Web`, net10.0): `PackageReference` to `Tjb.Web.Framework` + `Tjb.Web.Hosting` `1.1.0.190-dev` (Q2: no `Tjb.Shared`); `ProjectReference` to `Sample.Shared`/`Sample.Data`/`Sample.Migrations`. No `src/Tjb.*` refs (§6.4).*
- [x] 4.2 Write the thin-host `Program.cs`. — *Done: registers `SampleDbContext`, calls `AddAwsWebAppIdentity<SampleDbContext>`/`…GoogleAuth`/`…Email` (all from the Hosting package), host `AddRazorPages`/`AddServerSideBlazor`/`AddHealthChecks`, then `LogAwsWebAppAuthConfig` + `ApplyDatabaseMigrationsAsync<SampleDbContext>` + `UseAwsWebAppForwardedHeaders` + pipeline + `MapRazorPages` (Identity UI) + `MapBlazorHub` + `MapFallbackToPage("/_Host")`. Added `MapRazorPages()` (the reference omits it) so the RCL's Identity pages route.*
- [x] 4.3 Add `App.razor`/`_Imports.razor`/`_Host.cshtml`/`appsettings*.json` + home page. — *Done: `App.razor` uses `MainLayout` from the RCL (namespace `Tjb.Web.Shared` — the RCL's `RootNamespace` is `Tjb.Web`); `_Host.cshtml` links `_content/Tjb.Web.Framework/*` static assets + `Sample.Web.styles.css`; `Index.razor` landing. No layout/CSS copied locally.*
- [x] 4.4 Authenticated Guestbook page. — *Done: `[Authorize]` `Guestbook.razor` reads the user id from the auth state and lists/creates `GuestbookEntry` via the injected `SampleDbContext`.*
- [x] 4.5 Add the `Dockerfile`. — *Done: modeled on `src/Tjb.Web/Dockerfile`; build context = repo root (matches the reusable `deploy.yml`'s `docker build … .`), copies the self-contained `src/sample`, and takes `--build-arg GH_PACKAGES_TOKEN` for the framework-package restore. Not docker-built locally (no Docker here); exercised by `sample-ci-deploy`.*
- [x] 4.6 Min local boot config. — *Documented in `src/sample/README.md` + `appsettings.json` placeholders (`Authentication:Google`, `AwsSes`). Setting real user-secrets is the operator's step (Q3: real Google creds).*

## 5. Sample.Api (minimal)

- [x] 5.1 Create `src/sample/Sample.Api`. — *Done: minimal `Microsoft.NET.Sdk.Web` app — `/health` + Swagger (Swashbuckle) in dev, no auth surface. Mirrors `Tjb.Api`'s vestigial role for project-set shape parity.*

## 6. Local validation

- [x] 6.1 **Checkpoint:** standalone `Sample.sln` builds with `Tjb.sln` not loaded. — *PASSED: `dotnet build src/sample/Sample.sln -c Release` = 0 warnings / 0 errors, restoring `Tjb.Web.Framework*`/`Hosting` `1.1.0.190-dev` from the GitHub Packages feed. All 5 projects compile.*
- [ ] 6.2 **Checkpoint (operator):** run `Sample.Web`; `/Identity/Account/Login` renders the framework login page from the package. — *GATED on local MySQL + Google app (Q3). Build-verified; run is the operator's.*
- [ ] 6.3 **Checkpoint (operator):** sign in + exercise the Guestbook (create/persist/list via `SampleDbContext`). — *GATED on local MySQL + Google app.*
- [x] 6.4 Assert decoupling: no `ProjectReference` into `src/Tjb.*`. — *PASSED across all 5 csprojs (only `Sample.*` refs; framework consumed as packages).*
- [ ] 6.5 (Optional) `Sample.UiTests` Playwright smoke. — *Deferred to `sample-ci-deploy` (per task wording); not needed for local proof.*

## 7. Docs, validation, handoff

- [x] 7.1 README + reference the sample as the framework's reference consumer. — *Done: `src/sample/README.md`; root `README.md` "reusable deployment library" + `CLAUDE.md` reference the sample (below).*
- [x] 7.2 File framework packaging gaps. — *None found: the sample restored + built clean against the published `1.1.0.190-dev` packages (RCL static assets, layout, Identity area, hosting extensions, base Identity context all consumed without gaps). No patch release needed.*
- [x] 7.3 `openspec validate sample-solution-local --strict`. — *passed (see commit).*
- [x] 7.4 Record `ProjectName`/paths for `sample-ci-deploy`. — *Leaf/`project-name` = `sample`; `web-project-path` = `src/sample/Sample.Web`; Dockerfile = `src/sample/Sample.Web/Dockerfile` (context = repo root); image `sample-web`; framework pinned `1.1.0.190-dev`. Recorded here + in `src/sample/README.md`.*
- [ ] 7.5 Archive this change (`/opsx:archive`) once merged to `dev` and validated. — *Pending merge to dev.*
