## Context

`Tjb.Web` is a `Microsoft.NET.Sdk.Web` Blazor Server + Razor Pages + ASP.NET Identity app. It is the deployed front door (the README's "Lambda + API Gateway" is stale; `Tjb.Api` is vestigial). Today a single project holds:

- **Reusable surface** — `Areas/Identity/*` (scaffolded Identity pages incl. `ExternalLogin` which sends the post-registration SES email), `Shared/*` (`MainLayout`, `NavMenu`, `LoginDisplay`, `SurveyPrompt`), `Pages/_Host.cshtml`, `Pages/Error.cshtml`, `Pages/EmailTemplates/*`, `Services/*` (`AwsSesEmailService`, `ViewRenderService`, options + view models + the `IEmailService`/`IViewRenderService` contracts), `Data/RevalidatingIdentityAuthenticationStateProvider`, and `wwwroot/*`.
- **Host wiring** — all of `Program.cs`: Identity (`AddDefaultIdentity<IdentityUser>` + `AddEntityFrameworkStores<TjbDbContext>` + cookie events), Google OAuth (with its event-logging), SES email DI, `ForwardedHeaders` with `KnownProxies`/`KnownNetworks` cleared, and `ApplyDatabaseMigrations` (`EnsureCreatedAsync` + `MigrateAsync`, exception-swallowing).
- **App-specific content** — `Pages/Index.razor`, `Counter.razor`, `FetchData.razor`, `Data/WeatherForecastService`, and `TjbDbContext : IdentityDbContext<IdentityUser>` which also defines the task-management entities (in `Tjb.Data`).

The constraint that dominates this design: **a deployed ASP.NET app cannot be reused by reference.** The canonical reuse mechanism is a Razor Class Library (RCL) for shared Razor/static content plus a plain class library of `IServiceCollection`/`IApplicationBuilder` extension methods for the wiring. The host keeps only its `Program.cs` config, its own pages, and its own `DbContext`.

The acceptance bar is **non-lossiness**: after extraction, TaskManager must build, pass the CI unit-test filter, and deploy with byte-for-byte-equivalent runtime behavior (same login flow, OAuth callback, SES email, migrate-on-startup, URLs). This is the dogfooding proof that the packages are genuinely reusable.

## Goals / Non-Goals

**Goals:**

- Extract the reusable UI into an RCL `Tjb.Web.Framework` a host app consumes with zero copied files.
- Extract the host wiring into composable extension methods in `Tjb.Web.Hosting` so a host `Program.cs` is config + extension calls + its own page mapping.
- Provide an Identity-only base `DbContext` a consumer derives, so the consumer reuses the Identity schema but defines its own entities.
- Rewrite `Tjb.Web` to consume the packages and prove the extraction is non-lossy.
- Publish both packages to GitHub Packages on the existing feed/versioning conventions.
- Validate true package consumption (not just project linkage) by ending on a `PackageReference`, restoring from the feed, and deploying.

**Non-Goals:**

- The sample solution (`sample-solution-local`) and its CI deploy (`sample-ci-deploy`).
- Any change to CloudFormation templates or the deploy job (that is wilhelm's `make-deployment-stack-reusable`).
- Changing the exception-swallowing migrate-on-startup behavior, the Google-OAuth-only auth model, or `Tjb.Api`.
- Generalizing beyond what TaskManager needs (e.g., pluggable identity providers, Postgres). The framework encodes TaskManager's proven shape; broadening is a later change.
- Making the Identity user type generic across the whole stack. Phase scope keeps `IdentityUser` as the concrete user; a `TUser` generic is considered but deferred unless the sample forces it (see Open Questions).

## Decisions

### D1. Three assemblies: `Tjb.Web.Framework` (RCL), `Tjb.Web.Hosting` (extensions), and a slim host

- **`Tjb.Web.Framework`** — `Microsoft.NET.Sdk.Razor` RCL. Holds: `Areas/Identity/**`, `Shared/**`, `Pages/_Host.cshtml`, `Pages/Error.*`, `Pages/EmailTemplates/**`, `Services/**`, `RevalidatingIdentityAuthenticationStateProvider`, and `wwwroot/**`. An RCL compiles Razor content and exposes `wwwroot` under `_content/Tjb.Web.Framework/`. References the SDK packages the UI needs (`Microsoft.AspNetCore.Identity.UI`, `AWSSDK.SimpleEmail`, etc.).
- **`Tjb.Web.Hosting`** — plain `Microsoft.NET.Sdk` class library. Holds the extension methods. References `Tjb.Web.Framework` (so `AddAwsWebAppEmail` can register the framework's `AwsSesEmailService`/`ViewRenderService`) and the Identity-only base context assembly.
- **Host (`Tjb.Web`)** — references both. `Program.cs` becomes: build, register the app `DbContext`, `AddAwsWebAppIdentity<TjbDbContext>()`, `AddAwsWebAppGoogleAuth(config)`, `AddAwsWebAppEmail(config)`, `AddRazorPages()`/`AddServerSideBlazor()` + the app's own singletons, then `app.UseAwsWebAppForwardedHeaders()`, the standard pipeline, `app.MapFallbackToPage("/_Host")`, `await app.ApplyDatabaseMigrationsAsync<TjbDbContext>()`.

**Why three, not two:** the RCL (`Sdk.Razor`) and the extension library (`Sdk`) are different SDKs and have different reference graphs; merging them couples plain DI helpers to the Razor compilation pipeline unnecessarily. Keeping them separate also lets a future non-Blazor consumer take `Tjb.Web.Hosting` pieces without the RCL.

**Alternative considered — one combined package:** simpler to install but forces every consumer to pull the Razor toolchain even for headless wiring; rejected.

### D2. Identity-only base `DbContext` as an abstract base class

Introduce `AwsWebAppIdentityDbContext : IdentityDbContext<IdentityUser>` carrying only the Identity model configuration (whatever `OnModelCreating` customizations exist today for the Identity side) and **no** task entities. A consumer writes `class SampleDbContext : AwsWebAppIdentityDbContext { DbSet<Note> Notes ... ; override OnModelCreating(b){ base.OnModelCreating(b); /* own config */ } }`. `TjbDbContext` is rebuilt as `class TjbDbContext : AwsWebAppIdentityDbContext { /* task entities */ }`.

**Placement:** the base lives in `Tjb.Web.Framework` (or a tiny `Tjb.Web.Framework.Data` if we want to keep the Razor assembly free of the EF dependency — decided in tasks; default to a dedicated small assembly to avoid pulling Pomelo/EF into the RCL). It must NOT reference `Tjb.Data` (that would re-couple the framework to task entities).

**Why abstract base class over an `OnModelCreating` mixin/extension:** EF Core resolves the model from the concrete context type; a base class is the idiomatic, well-supported way to share Identity configuration and `DbSet`s. A static `modelBuilder.ApplyAwsWebAppIdentity()` mixin is the alternative — more flexible (lets a consumer use a different base) but pushes the burden of "remember to call it" onto every consumer and doesn't share the `IdentityDbContext<IdentityUser>` base cleanly. Rejected for now; revisit if the sample needs a non-`IdentityUser` user.

**Migration-equivalence guard:** after `TjbDbContext` derives the base, an `ef migrations add` dry-run MUST produce an empty diff. If it doesn't, the base's `OnModelCreating` differs from the original and must be reconciled before proceeding. This is the phase-3 checkpoint.

### D3. The hosting extension surface

In `Tjb.Web.Hosting` (namespace `Microsoft.Extensions.DependencyInjection` for discoverability, matching the existing `GoogleExtensions` usage):

- `IServiceCollection AddAwsWebAppIdentity<TContext>(this IServiceCollection, Action<IdentityOptions>? = null)` — `AddDefaultIdentity<IdentityUser>` (default `RequireConfirmedAccount = true`) + `AddEntityFrameworkStores<TContext>` + `ConfigureApplicationCookie` with the cookie event logging + the `RevalidatingIdentityAuthenticationStateProvider` registration. `TContext` is the consumer's context (constrained to `DbContext`).
- `AuthenticationBuilder AddAwsWebAppGoogleAuth(this IServiceCollection, IConfiguration)` — `AddAuthentication().AddGoogle(...)` reading `Authentication:Google:ClientId`/`ClientSecret`, with the OnCreatingTicket/OnRemoteFailure/OnTicketReceived logging preserved.
- `IServiceCollection AddAwsWebAppEmail(this IServiceCollection, IConfiguration)` — `Configure<AwsSesOptions>` + the `IAmazonSimpleEmailService` factory (region-empty ⇒ auto-detect) + `IEmailService` + `IViewRenderService` + `AddHttpContextAccessor`.
- `IApplicationBuilder UseAwsWebAppForwardedHeaders(this IApplicationBuilder)` — the `XForwardedFor | XForwardedProto` config with `KnownProxies`/`KnownNetworks` cleared.
- `Task ApplyDatabaseMigrationsAsync<TContext>(this WebApplication)` — `EnsureCreatedAsync` + `MigrateAsync`, log-and-swallow on failure.

Page/Blazor registration (`AddRazorPages`, `AddServerSideBlazor`, `AddHealthChecks`) stays in the host — it's standard ASP.NET and a host may want to tweak it. The diagnostic Google-config-status logging block becomes part of `AddAwsWebAppGoogleAuth` or an optional `LogAwsWebAppAuthConfig` helper.

**Why `Microsoft.Extensions.DependencyInjection` namespace:** consumers get the extensions without an extra `using`, consistent with how AWS/Identity SDKs surface their `Add*` methods.

### D4. Phased project-reference → package-reference cutover

Phases 1–3 wire `Tjb.Web` to the new projects via **`ProjectReference`** so the refactor is iterable in one solution without round-tripping through the NuGet feed. Phase 4 swaps to **`PackageReference`** against the published packages, restores from GitHub Packages, builds, and deploys on a throwaway feature branch. This proves the package boundary is real (no leaked `InternalsVisibleTo`, no implicit project-graph dependency) — the same boundary the sample will consume across.

**Why end on PackageReference in this change** rather than deferring to `sample-solution-local`: the non-lossiness proof is only complete if TaskManager itself deploys from the *packaged* artifacts. Otherwise a project-reference-only success could hide a packaging gap (missing static assets, wrong content paths) that would surface first in the sample.

### D5. CI packaging — additive pack steps only

Add `Pack Tjb.Web.Framework` and `Pack Tjb.Web.Hosting` steps next to the existing `Pack Tjb.*` steps in `zbuild.yml`, following the same GitVersion SemVer + branch-suffix pattern. The `publish-nuget` job already globs `./nupkgs/`, so no publish-job change. RCL packaging needs the static web assets included in the pack — verify the `.nupkg` contains `staticwebassets` and the Razor views (inspect the produced package in phase 2, as the deployment-stack change does for its templates).

## Risks / Trade-offs

- **[Risk] Static web assets or Razor content don't pack/resolve correctly from the RCL** (broken CSS, missing `_content/...` paths, blank login page). → Mitigate: inspect the `.nupkg` contents in phase 2; the phase-1 checkpoint runs the app locally and visually confirms login + layout; phase 4 deploys and runs UI tests against the real `_content/` paths.
- **[Risk] The Identity base context's `OnModelCreating` subtly differs from today's, producing a spurious migration or — worse — a schema drift that only appears at deploy-time `MigrateAsync`.** → Mitigate: D2's empty-diff guard is a hard phase-3 gate; do not proceed past it.
- **[Risk] `ExternalLogin.cshtml.cs` (the SES-email-sending Identity page) has tight coupling to host services and resists moving into the RCL.** → Mitigate: it depends on `IEmailService`/`IViewRenderService`, both of which move into the framework too; if any host-specific binding remains, expose it via an injected abstraction rather than leaving the page in the host.
- **[Risk] Namespace/area collisions** when both the package and a future host define `Areas/Identity`. → Mitigate: the framework owns `Areas/Identity`; hosts that need to override a single page use the standard RCL override mechanism (a same-path file in the host wins). Documented in the consuming notes.
- **[Trade-off] Three new assemblies + a package boundary add build/restore overhead and a versioning surface the project didn't have for its web tier.** → Acceptable; it's the cost of reuse and mirrors what `make-deployment-stack-reusable` accepts for the deploy tier.
- **[Risk] This change and wilhelm's `make-deployment-stack-reusable` both touch `zbuild.yml` (pack steps).** → Mitigate: the edits are additive and in different regions of the file (pack steps vs. deploy job); coordinate merge order with wilhelm, integrate via direct merge (no PRs).

## Migration Plan

Sequenced as five phases, each with a checkpoint that must pass before the next:

1. **RCL carve-out.** Create `Tjb.Web.Framework`; move `Areas/Identity`, `Shared`, `_Host`/`Error`/`EmailTemplates`, `Services`, the auth-state provider, and `wwwroot`. `Tjb.Web` references it via `ProjectReference`. **Checkpoint:** app runs locally, login page renders, layout/CSS intact.
2. **Hosting extraction.** Create `Tjb.Web.Hosting`; move the wiring into the D3 extensions; reduce `Program.cs` to extension calls. **Checkpoint:** local run + CI unit-test filter green; OAuth and SES wiring behaviorally unchanged.
3. **Identity-only base context.** Introduce `AwsWebAppIdentityDbContext`; rebuild `TjbDbContext` to derive it. **Checkpoint:** `ef migrations add` dry-run yields an empty diff; `MigrateAsync` applies cleanly against a fresh DB.
4. **Package cutover.** Swap `Tjb.Web` to `PackageReference`; restore from GitHub Packages; deploy on a throwaway feature branch. **Checkpoint:** restore resolves from the feed, build succeeds, UI tests pass against the deployed URL.
5. **Docs + release.** Package metadata, README/CLAUDE.md updates, `openspec validate --strict`, tag a pre-release version.

**Rollback:** each phase is a revertible commit; phases 1–3 never leave a non-building tree; phase 4's package swap reverts to `ProjectReference` if the feed restore fails; the new projects can stay (unused) if `Tjb.Web` reverts.

## Open Questions

1. **Generic user type?** Keep `IdentityUser` concrete, or make the base `AwsWebAppIdentityDbContext<TUser>` to let a consumer use a custom user? Default: concrete `IdentityUser` for this change; promote to generic only if `sample-solution-local` needs a custom user. Resolve before phase 3 freezes the base shape.
2. **Where does `AwsWebAppIdentityDbContext` live** — inside the RCL or a dedicated tiny `Tjb.Web.Framework.Data` assembly? Default to the dedicated assembly to keep EF/Pomelo out of the Razor RCL. Confirm in phase 1.
3. **Package names** — `Tjb.Web.Framework` / `Tjb.Web.Hosting` keep the `Tjb` prefix (consistent with existing packages) vs. a neutral `YadaYada.AwsWebApp.Web*` (consistent with wilhelm's `YadaYada.AwsWebApp.DeploymentStack`). Pick before phase 5 tags a version; once tagged it's hard to change. Recommend aligning with wilhelm's `YadaYada.AwsWebApp.*` naming for the reusable tier.
4. **Does `NavMenu` belong in the RCL** given its links are TaskManager-specific (Counter/FetchData)? Likely the shell/chrome is framework but the link set is host-provided via a slot/parameter. Decide in phase 1; worst case the host overrides `NavMenu`.

## Open-Question resolutions (implementation, 2026-06-09)

- **Q1 (generic user type):** Resolved — keep `IdentityUser` concrete. No `TUser` generic this change.
- **Q2 (base-context placement):** Resolved — dedicated `src/Tjb.Web.Framework.Data` assembly so the Razor RCL carries no EF/Pomelo dependency. It references only `Microsoft.AspNetCore.Identity.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore` (no Pomelo — the provider stays in the consumer's `OnConfiguring`).
- **Q3 (package names):** Deferred to phase 5 per tasks; using `Tjb.Web.Framework` / `Tjb.Web.Framework.Data` / `Tjb.Web.Hosting` (keep `Tjb` prefix) for now.
- **Q4 (`NavMenu`):** Resolved — `NavMenu` moves into the RCL **with its current TaskManager links** (non-lossy for `Tjb.Web`). `MainLayout` references it directly, so it must live alongside the shell. Parameterizing the link set / host-override is deferred; the standard RCL same-path override is the escape hatch a future consumer (the sample) uses.

### Namespace strategy (non-lossiness choice)

The moved code keeps its existing `Tjb.Web.*` namespaces; the RCL sets `<RootNamespace>Tjb.Web</RootNamespace>` so Razor components compile to `Tjb.Web.Shared` etc. This keeps `Tjb.Web`'s `using`/`@using` statements and `_Host.cshtml`/`App.razor` references valid with minimal churn. Namespaces need not equal the assembly/package name; the package is `Tjb.Web.Framework` while the types remain under `Tjb.Web.*`. Neutral-namespace renaming is cosmetic and out of scope for the non-lossiness bar.

### `App.razor` / `_Host.cshtml` placement (deviation from the file list)

The proposal/spec list `_Host.cshtml` among the RCL contents. In Blazor Server, `App.razor` (the `Router` root) discovers routable components via `AppAssembly`/`AdditionalAssemblies`, and `_Host.cshtml` instantiates `typeof(App)`. Both are intrinsically coupled to the *host's* page assembly — moving them into the RCL would stop the host's own pages (`Counter`/`Index`/`FetchData`) from being routed without an `AdditionalAssemblies` workaround the RCL cannot express (it can't name host types). They are therefore **kept in the host** as the thin Blazor bootstrap; everything else moves. This satisfies every scenario in `spec.md` (Identity UI served from the RCL, static assets under `_content/Tjb.Web.Framework/`, shared layout renders, migrate-on-startup) and the byte-for-byte runtime bar. `Pages/Error.*` and `Pages/EmailTemplates/**` have no such coupling and **do** move to the RCL.
