## 1. Prereqs

- [ ] 1.1 Confirm `extract-web-framework-package` has published `Tjb.Web.Framework` + `Tjb.Web.Hosting` (and the Identity-only base context) to GitHub Packages; record the exact version to consume.
- [ ] 1.2 Branch from `dev` with the spec's exact name: `git checkout dev; git pull; git checkout -b sample-solution-local`.
- [ ] 1.3 Resolve design Open Questions: sample subdomain/`ProjectName` (Q1) before the Dockerfile lands; `Sample.Shared` vs consume `Tjb.Shared` (Q2); minimum local boot config (Q3).

## 2. Solution + feed scaffolding

- [ ] 2.1 Create `src/sample/Sample.sln` and a `src/sample/nuget.config` (or documented `dotnet nuget add source`) pointing at the GitHub Packages feed with a `read:packages` PAT.
- [ ] 2.2 Add a `src/sample/README.md` documenting feed/PAT setup and the local run steps.

## 3. Sample.Data + Sample.Migrations

- [ ] 3.1 Create `src/sample/Sample.Data/Sample.Data.csproj` (`net10.0`); add `PackageReference` to `Tjb.Web.Framework` (for the Identity base) and `Tjb.Shared`. Verify no `ProjectReference` into `src/Tjb.*`.
- [ ] 3.2 Define the Guestbook domain: `GuestbookEntry` (Id, AuthorUserId, Message, CreatedUtc) and `SampleDbContext : AwsWebAppIdentityDbContext` with `DbSet<GuestbookEntry>` and `OnModelCreating` calling `base` first.
- [ ] 3.3 Create `src/sample/Sample.Migrations` mirroring `Tjb.Migrations`: `IDesignTimeDbContextFactory<SampleDbContext>` reading its own `appsettings.json`, `MigrationsAssembly("Sample.Migrations")` wiring, and a standalone migrate/seed `Program`.
- [ ] 3.4 Generate the initial migration: `dotnet ef migrations add InitialCreate --project src/sample/Sample.Migrations --startup-project src/sample/Sample.Migrations`.
- [ ] 3.5 **Checkpoint:** run `dotnet run --project src/sample/Sample.Migrations` against a fresh local MySQL; confirm Identity + `GuestbookEntries` tables are created.

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
