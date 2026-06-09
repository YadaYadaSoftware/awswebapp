## Why

`Tjb.Web` is the deployed application, but it is also the only place the project's reusable web surface exists — ASP.NET Identity pages, the Blazor layout/shell, the SES email machinery, the ALB-aware hosting wiring — all welded into one `Microsoft.NET.Sdk.Web` project alongside TaskManager-specific content (`Counter`/`FetchData`/`Index`, `WeatherForecastService`, and a `TjbDbContext` that mixes Identity with task entities). Because that surface lives in an *application* and not a *library*, another .NET web app cannot reuse it except by copy-paste. That blocks the larger goal: spinning up more than one multi-regional web app from this framework and slipstreaming `Tjb` bug fixes into all of them. This change factors the reusable surface out so it can be consumed via NuGet, and rewrites `Tjb.Web` to consume it — proving the extraction is non-lossy.

This is the **first** of a coordinated set of changes:

1. **`extract-web-framework-package`** (this change) — factor the reusable web surface into packages; `Tjb.Web` becomes a thin consumer.
2. **`sample-solution-local`** — scaffold `src/sample` (`Sample.Web`/`Sample.Data`/`Sample.Migrations`/optional `Sample.Api`) consuming the framework via NuGet only; build/run/test locally.
3. **`sample-ci-deploy`** — deploy the sample through CI to `dev`/`alpha`/`beta`/`app`; depends on the separate `make-deployment-stack-reusable` change.
4. **`framework-slipstream-upgrade`** — prove a `Tjb` framework fix flows into the sample via a package version bump.

## What Changes

- **New Razor Class Library `Tjb.Web.Framework`** (`Microsoft.NET.Sdk.Razor`) holding the reusable UI: the `Areas/Identity` scaffolded pages, the `Shared/` layout+shell (`MainLayout`, `NavMenu`, `LoginDisplay`), `Pages/_Host.cshtml` + `Error` + `EmailTemplates`, the `Services/` SES email + view-render, the `RevalidatingIdentityAuthenticationStateProvider` auth-state provider, and `wwwroot` static web assets. RCLs ship Razor Pages/views/components and static assets that a host app picks up automatically.
- **New hosting library `Tjb.Web.Hosting`** exposing extension methods that encapsulate today's `Program.cs` wiring: `AddAwsWebAppIdentity` (default Identity + EF stores + cookie events), `AddAwsWebAppGoogleAuth` (Google OAuth + its event logging), `AddAwsWebAppEmail` (SES options + `IAmazonSimpleEmailService` + `IEmailService` + `IViewRenderService`), `UseAwsWebAppForwardedHeaders` (the `KnownProxies`/`KnownNetworks`-cleared ALB config), and `ApplyDatabaseMigrationsAsync` (the `EnsureCreatedAsync` + `MigrateAsync`, exception-swallowing helper). A host `Program.cs` shrinks to: register the app's own `DbContext`, call the framework extensions, map the app's own pages.
- **New Identity-only base `DbContext` abstraction** so a consumer app can derive its own context (with its own entities) while reusing the Identity tables and configuration. Today `TjbDbContext : IdentityDbContext<IdentityUser>` also carries the task entities; the framework must offer the Identity half *without* them. The exact shape (abstract `AwsWebAppIdentityDbContext` base class vs. an `OnModelCreating` mixin) is settled in design.
- **`Tjb.Web` rewritten** to consume `Tjb.Web.Framework` + `Tjb.Web.Hosting`, keeping only TaskManager's own pages, `WeatherForecastService`, and a slimmed `TjbDbContext` that derives the framework Identity base. This is the dogfooding proof.
- **Publish the two new packages** to GitHub Packages on the same NuGet feed and with the same SemVer + branch-suffix conventions as the existing `Tjb.*` packages (one added pack step per package in CI; no publish-job change needed).
- **NOT BREAKING for operators**: the deployed surface, URLs, login flow, OAuth callback, SES email, and migrate-on-startup behavior are unchanged. The change is an internal restructuring whose acceptance bar is *byte-for-byte-equivalent runtime behavior*.

## Capabilities

### New Capabilities

- `web-framework-package`: How `Tjb.Web`'s reusable front-end surface (Identity UI, layout/shell, email, auth-state, static assets) and its host wiring (Identity, Google OAuth, ALB forwarded-headers, SES, migrate-on-startup) are packaged as a Razor Class Library plus a hosting-extensions library and published to GitHub Packages, so a host app provides only configuration and its own pages. Covers: what belongs in the RCL vs. the hosting library vs. the host; the extension-method surface; the Identity-only base `DbContext` contract a consumer derives; the package metadata/versioning; and the non-lossiness guarantee that `Tjb.Web` deploys identically after the extraction.

### Modified Capabilities

_None._ This change restructures where the reusable code lives; it does not redefine the requirements of any existing capability. `aws-ses-email-integration`, `email-confirmation-workflow`, and `email-template-system` continue to describe the same observable behavior — the code that implements them simply moves into `Tjb.Web.Framework`, which `Tjb.Web` still consumes.

## Impact

**New artifacts:**
- `src/Tjb.Web.Framework/` — Razor Class Library (RCL). Receives most of `src/Tjb.Web/Areas/Identity`, `Shared/`, `Services/`, `Pages/_Host.cshtml`/`Error`/`EmailTemplates`, the auth-state provider, and `wwwroot`. Published as NuGet `Tjb.Web.Framework`.
- `src/Tjb.Web.Hosting/` — class library of `IServiceCollection`/`IApplicationBuilder` extension methods. Published as NuGet `Tjb.Web.Hosting`.
- Identity-only base `DbContext` (placement decided in design — likely `Tjb.Web.Framework` or a small `Tjb.Web.Framework.Data` assembly so it carries no task-entity dependency).

**Modified in this repo:**
- `src/Tjb.Web/` — `Program.cs` collapses to configuration + framework-extension calls + app-specific page mapping; `Areas/Identity`, `Shared/`, `Services/`, and the moved `Pages` are deleted from here and resolved from the packages. `Tjb.Web.csproj` references the framework packages (project reference during phases 1–3, package reference in phase 4).
- `src/Tjb.Data/` — `TjbDbContext` slims to derive the framework Identity base + the task entities only.
- `Tjb.sln` — add the two new projects.
- `.github/workflows/zbuild.yml` — add a pack step per new package alongside the existing `Pack Tjb.*` steps.
- `README.md` / `CLAUDE.md` — note that the web framework now lives in distributable packages.

**Explicitly out of scope:**
- The sample solution (`src/sample`) — that is `sample-solution-local`.
- Any change to the CloudFormation templates or the deploy workflow's deploy job — the reusable deployment stack is wilhelm's separate `make-deployment-stack-reusable` change.
- Changing the exception-swallowing migrate-on-startup behavior, or anything in `Tjb.Api`.
