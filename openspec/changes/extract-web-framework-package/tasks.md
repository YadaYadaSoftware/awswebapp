## 1. Prereqs and audit

- [ ] 1.1 Branch from `dev` with the spec's exact name: `git checkout dev; git pull; git checkout -b extract-web-framework-package`.
- [ ] 1.2 Inventory the reusable surface in `src/Tjb.Web`: list every file under `Areas/Identity`, `Shared`, `Pages` (`_Host`, `Error`, `EmailTemplates`), `Services`, `Data/RevalidatingIdentityAuthenticationStateProvider*`, and `wwwroot`. Mark each as framework / host / undecided (e.g. `NavMenu` link set — see design Open Question 4).
- [ ] 1.3 Resolve design Open Questions before they block a phase: package names (Q3) before phase 5; generic-vs-concrete user type (Q1) before phase 3; base-context placement (Q2) before phase 1; `NavMenu` ownership (Q4) before phase 1.
- [ ] 1.4 Capture a behavior baseline of the current app to compare against after the refactor: login page render, Google sign-in flow, the post-registration SES confirmation email, and `MigrateAsync` against a fresh local MySQL.

## 2. Phase 1 — Carve out the `Tjb.Web.Framework` RCL

- [ ] 2.1 Create `src/Tjb.Web.Framework/Tjb.Web.Framework.csproj` as `Microsoft.NET.Sdk.Razor`, `net10.0`, packable, with the package metadata fields (PackageId, Authors, etc.) and the SDK references the UI needs (`Microsoft.AspNetCore.Identity.UI`, `AWSSDK.SimpleEmail`, EF/Pomelo only if the moved code needs it).
- [ ] 2.2 Move `Areas/Identity/**`, `Shared/**` (`MainLayout`, `LoginDisplay`, `SurveyPrompt`, and `NavMenu` per Q4), `Pages/_Host.cshtml`, `Pages/Error.*`, `Pages/EmailTemplates/**`, `Services/**`, and `Data/RevalidatingIdentityAuthenticationStateProvider*` into the RCL. Move `wwwroot/**` reusable assets in.
- [ ] 2.3 Decide and create the Identity-base assembly location (Q2): either inside the RCL or a dedicated `src/Tjb.Web.Framework.Data`. Add it to the solution; do not reference `Tjb.Data` from it.
- [ ] 2.4 Add `Tjb.Web.Framework` (and the base-context assembly) to `Tjb.sln`. Reference them from `Tjb.Web` via `ProjectReference`.
- [ ] 2.5 Fix up namespaces/`_Imports.razor`/`@using` so the moved Razor content compiles from the RCL. Ensure `App.razor`/`_Host.cshtml` resolve the framework layout.
- [ ] 2.6 **Checkpoint:** `dotnet run --project src/Tjb.Web` locally; confirm the login page renders, the shared layout + CSS are intact, and `/Identity/Account/Login` serves the framework page. Compare visually to the 1.4 baseline.

## 3. Phase 2 — Carve out `Tjb.Web.Hosting` extensions

- [ ] 3.1 Create `src/Tjb.Web.Hosting/Tjb.Web.Hosting.csproj` as `Microsoft.NET.Sdk`, `net10.0`, packable, referencing `Tjb.Web.Framework` and the Identity-base assembly. Add to `Tjb.sln`.
- [ ] 3.2 Implement `AddAwsWebAppIdentity<TContext>` (default Identity + EF stores + cookie events + auth-state provider) in namespace `Microsoft.Extensions.DependencyInjection`.
- [ ] 3.3 Implement `AddAwsWebAppGoogleAuth(IConfiguration)` preserving the OnCreatingTicket/OnRemoteFailure/OnTicketReceived logging, plus the optional Google-config-status logging helper.
- [ ] 3.4 Implement `AddAwsWebAppEmail(IConfiguration)` (SES options + `IAmazonSimpleEmailService` region-empty-autodetect factory + `IEmailService` + `IViewRenderService` + `AddHttpContextAccessor`).
- [ ] 3.5 Implement `UseAwsWebAppForwardedHeaders` (`XForwardedFor | XForwardedProto`, `KnownProxies`/`KnownNetworks` cleared) and `ApplyDatabaseMigrationsAsync<TContext>` (EnsureCreated + Migrate, log-and-swallow).
- [ ] 3.6 Rewrite `src/Tjb.Web/Program.cs` to: register `TjbDbContext`, call the four `Add*` extensions, add the host's own `AddRazorPages`/`AddServerSideBlazor`/`AddHealthChecks`/app singletons, then `UseAwsWebAppForwardedHeaders` + standard pipeline + `MapFallbackToPage("/_Host")` + `await app.ApplyDatabaseMigrationsAsync<TjbDbContext>()`.
- [ ] 3.7 **Checkpoint:** local run; Google OAuth sign-in completes and the SES confirmation email sends (against a verified sandbox recipient); `dotnet test --filter "FullyQualifiedName!~Tjb.UiTests"` is green.

## 4. Phase 3 — Identity-only base DbContext

- [ ] 4.1 Implement `AwsWebAppIdentityDbContext : IdentityDbContext<IdentityUser>` carrying only the Identity-side `OnModelCreating` config (no task entities) in the base-context assembly.
- [ ] 4.2 Rebuild `Tjb.Data`'s `TjbDbContext` to derive `AwsWebAppIdentityDbContext` and define only the task-management entities; call `base.OnModelCreating(builder)` first.
- [ ] 4.3 **Checkpoint (hard gate):** run `dotnet ef migrations add _NoOpProbe --project src/Tjb.Migrations --startup-project src/Tjb.Migrations` and confirm the generated migration is empty (no schema diff). Delete the probe migration. If non-empty, reconcile the base's `OnModelCreating` until the diff is empty before proceeding.
- [ ] 4.4 Apply migrations against a fresh local MySQL (`dotnet run --project src/Tjb.Migrations`) and confirm the Identity + task tables are created as before.

## 5. Phase 4 — Package cutover (prove true consumption)

- [ ] 5.1 Add `Pack Tjb.Web.Framework` and `Pack Tjb.Web.Hosting` steps to `.github/workflows/zbuild.yml` next to the existing `Pack Tjb.*` steps, following the GitVersion SemVer + branch-suffix convention. Coordinate the edit region with wilhelm's `make-deployment-stack-reusable` pack changes.
- [ ] 5.2 `dotnet pack` both projects locally to `./nupkgs`; unzip and confirm each `.nupkg` contains the expected Razor content and (for the RCL) `staticwebassets` / `_content` assets and `wwwroot`.
- [ ] 5.3 Push the branch; confirm CI publishes both packages to GitHub Packages with the branch-suffixed version.
- [ ] 5.4 Swap `Tjb.Web.csproj` from `ProjectReference` to `PackageReference` for `Tjb.Web.Framework` + `Tjb.Web.Hosting`, pinned to the just-published branch-suffixed version. `dotnet restore` from the GitHub Packages feed; build.
- [ ] 5.5 **Checkpoint:** push and let the branch deploy; confirm the deployed app at `https://extract-web-framework-package.{DOMAIN_NAME}` renders login + layout (validating `_content/` asset paths from the package), Google sign-in works, the SES email sends, and the UI tests pass against the deployed URL.

## 6. Phase 5 — Docs, validation, release

- [ ] 6.1 Finalize package metadata (id per Q3, description, repository URL, tags) on both projects.
- [ ] 6.2 Update `README.md` and `CLAUDE.md` to note the web framework now ships as `Tjb.Web.Framework` + `Tjb.Web.Hosting` packages and that `Tjb.Web` is their first consumer; cross-link the roadmap (this → `sample-solution-local` → `sample-ci-deploy` → `framework-slipstream-upgrade`).
- [ ] 6.3 Grep audit: `src/Tjb.Web` contains no `Areas/Identity` pages, no `Shared/` layout, and no `Services/` email code of its own.
- [ ] 6.4 `openspec validate extract-web-framework-package --strict` and resolve any issues.
- [ ] 6.5 Verify each scenario in `specs/web-framework-package/spec.md` against the implementation (RCL UI resolves, extensions compose, consumer-derivable Identity base, packages publish + restore, non-lossy TaskManager deploy).
- [ ] 6.6 Tag a pre-release version of the two packages; record the version for `sample-solution-local` to consume.
- [ ] 6.7 Archive this change per the experimental workflow (`/opsx:archive`) once merged to `dev` and validated.
