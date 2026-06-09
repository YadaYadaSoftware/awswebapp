# web-framework-package Specification

## Purpose
TBD - created by archiving change extract-web-framework-package. Update Purpose after archive.
## Requirements
### Requirement: Reusable UI ships as a Razor Class Library

The framework SHALL package the reusable front-end surface — the `Areas/Identity` pages, the `Shared/` layout and shell components, the `_Host`/`Error`/`EmailTemplates` pages, and the `wwwroot` static web assets — as a Razor Class Library named `Tjb.Web.Framework` (`Microsoft.NET.Sdk.Razor`), such that a host web application that references it picks up those pages, components, and static assets without copying any files.

#### Scenario: Host renders framework Identity UI without local copies

- **WHEN** a host web app references `Tjb.Web.Framework` and contains no `Areas/Identity` pages of its own
- **THEN** navigating to `/Identity/Account/Login` SHALL serve the framework's login page

#### Scenario: Host serves framework static assets

- **WHEN** a host web app references `Tjb.Web.Framework`
- **THEN** the framework's `wwwroot` assets SHALL be served under the `_content/Tjb.Web.Framework/` static-asset path without being copied into the host project

#### Scenario: Host renders the shared layout

- **WHEN** a host page declares the framework `MainLayout` as its layout
- **THEN** the page SHALL render inside that layout with the shared nav/shell, with no layout files present in the host project

### Requirement: Host wiring ships as composable extension methods

The framework SHALL package the application startup wiring as a library named `Tjb.Web.Hosting` exposing extension methods that a host's `Program.cs` calls in place of inline configuration: registering ASP.NET Identity with EF stores and cookie events, Google OAuth, the SES email services (`IEmailService`, `IViewRenderService`, `IAmazonSimpleEmailService`), the ALB-aware forwarded-headers configuration, and a migrate-on-startup helper. Each extension SHALL be independently callable.

#### Scenario: Host composes the framework via extension calls

- **WHEN** a host `Program.cs` registers its own `DbContext` and then calls the framework's Identity, Google-auth, email, and forwarded-headers extensions
- **THEN** the application SHALL start with Identity, Google OAuth, SES email, and ALB forwarded-headers configured identically to inline wiring, with no inline duplication in the host

#### Scenario: Forwarded-headers helper preserves ALB behavior

- **WHEN** the host calls the forwarded-headers extension
- **THEN** `ForwardedHeaders` SHALL include `XForwardedFor | XForwardedProto` and the `KnownProxies` and `KnownNetworks` collections SHALL be cleared, so the OAuth `/signin-google` callback sees HTTPS behind the ALB

#### Scenario: Migrate-on-startup helper swallows failures

- **WHEN** the host calls the migrate-on-startup helper and migration throws
- **THEN** the helper SHALL log the error and allow the application to continue starting, matching the current non-throwing behavior

### Requirement: Consumer apps derive an Identity-only base DbContext

The framework SHALL provide an Identity-only `DbContext` base that configures the ASP.NET Identity model without any TaskManager task-management entities, so a consumer application can define its own `DbContext` deriving from it and add its own entities while reusing the Identity schema.

#### Scenario: A consumer context inherits Identity without task entities

- **WHEN** a consumer defines a `DbContext` deriving the framework Identity base and adds only its own entities
- **THEN** the resulting model SHALL contain the ASP.NET Identity tables and the consumer's entities, and SHALL NOT contain TaskManager task-management entities

#### Scenario: TaskManager context derives the same base

- **WHEN** `TjbDbContext` is rebuilt to derive the framework Identity base plus the task-management entities
- **THEN** an EF migrations diff against the pre-change schema SHALL be empty (no new migration is required)

### Requirement: The framework packages publish to GitHub Packages

The framework SHALL publish `Tjb.Web.Framework` and `Tjb.Web.Hosting` to the same GitHub Packages NuGet feed as the existing `Tjb.*` packages, following the same SemVer and branch-suffix versioning conventions, so consumers install them the same way they install `Tjb.Shared`.

#### Scenario: Packages publish on CI build

- **WHEN** CI builds a branch that produces NuGet packages
- **THEN** `Tjb.Web.Framework` and `Tjb.Web.Hosting` SHALL be packed and published to GitHub Packages with the branch-appropriate version (suffixed for non-`app` branches, unsuffixed on `app`)

#### Scenario: A host restores the framework from the feed

- **WHEN** a host app adds a `PackageReference` to `Tjb.Web.Framework` and `Tjb.Web.Hosting` and runs `dotnet restore` against the GitHub Packages feed
- **THEN** restore SHALL resolve both packages and the host SHALL build against them

### Requirement: The extraction is non-lossy for TaskManager

`Tjb.Web` SHALL be rewritten to consume `Tjb.Web.Framework` and `Tjb.Web.Hosting` instead of carrying the reusable surface inline, with no change to its observable behavior: the same login flow, Google OAuth `/signin-google` callback, SES confirmation email, migrate-on-startup, and deployed URLs.

#### Scenario: TaskManager no longer carries the reusable surface locally

- **WHEN** the refactor is complete
- **THEN** `src/Tjb.Web/` SHALL contain no `Areas/Identity` pages, no `Shared/` layout components, and no `Services/` email code of its own — those SHALL resolve from the packages

#### Scenario: TaskManager builds and its unit tests pass

- **WHEN** CI runs `dotnet build` and the unit-test filter against the rewritten `Tjb.Web`
- **THEN** the build SHALL succeed and the unit tests SHALL pass

#### Scenario: TaskManager deploys with identical runtime behavior

- **WHEN** the rewritten `Tjb.Web` is deployed to a test branch environment
- **THEN** the login page, Google OAuth sign-in, post-registration SES confirmation email, and migrate-on-startup SHALL behave identically to the pre-change deployment, and the UI tests SHALL pass against the deployed URL
