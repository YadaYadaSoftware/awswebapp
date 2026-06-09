## ADDED Requirements

### Requirement: The sample consumes the framework via NuGet only

The sample solution SHALL reference `Tjb.Web.Framework`, `Tjb.Web.Hosting`, and `Tjb.Shared` exclusively through `PackageReference` resolved from the GitHub Packages feed, and SHALL contain no `ProjectReference` to any project under `src/Tjb.*`.

#### Scenario: No project references into the Tjb source tree

- **WHEN** the sample projects are inspected
- **THEN** none of them SHALL contain a `ProjectReference` whose path resolves into `src/Tjb.*`, and the framework SHALL be referenced only via `PackageReference`

#### Scenario: Restore resolves the framework from the feed

- **WHEN** `dotnet restore` runs on `src/sample/Sample.sln` with the GitHub Packages feed configured
- **THEN** restore SHALL resolve `Tjb.Web.Framework`, `Tjb.Web.Hosting`, and `Tjb.Shared` from the feed and the solution SHALL build

### Requirement: The sample defines its own data model on the framework Identity base

`Sample.Data` SHALL define `SampleDbContext` deriving the framework's Identity-only base `DbContext` and adding the sample's own Guestbook/Notes entities, with EF migrations in `Sample.Migrations`.

#### Scenario: SampleDbContext carries Identity plus sample entities only

- **WHEN** the `SampleDbContext` model is built
- **THEN** it SHALL include the ASP.NET Identity tables (from the framework base) and the sample's Guestbook/Notes entities, and SHALL NOT include any TaskManager task-management entities

#### Scenario: Sample migrations create the schema

- **WHEN** the `Sample.Migrations` runner is executed against a fresh local MySQL database
- **THEN** it SHALL create the Identity tables and the sample's entity tables and exit successfully

### Requirement: The sample runs locally with framework auth and its own data

`Sample.Web` SHALL be a thin host whose `Program.cs` registers `SampleDbContext` and composes the framework via its hosting extensions, and SHALL serve at least one authenticated page that performs CRUD over the sample's entities.

#### Scenario: Login renders from the framework package

- **WHEN** `Sample.Web` is run locally and a user navigates to `/Identity/Account/Login`
- **THEN** the framework's login page SHALL render, served from the `Tjb.Web.Framework` package with no Identity pages present in the sample source

#### Scenario: Authenticated sample page performs CRUD

- **WHEN** an authenticated user opens the sample Guestbook page and creates an entry
- **THEN** the entry SHALL be persisted via `SampleDbContext` and shown back in the list

#### Scenario: Host Program.cs has no inline framework wiring

- **WHEN** `Sample.Web/Program.cs` is inspected
- **THEN** Identity, Google OAuth, SES email, and forwarded-headers configuration SHALL be expressed as calls to the framework hosting extensions, not inline duplication of `Tjb.Web`'s original wiring

### Requirement: The sample builds independently of the Tjb solution

The sample SHALL have its own `Sample.sln` so it builds and tests without `Tjb.sln`, reinforcing that a consumer project is self-contained.

#### Scenario: Sample solution builds on its own

- **WHEN** `dotnet build src/sample/Sample.sln` runs without `Tjb.sln` loaded
- **THEN** the build SHALL succeed using only the published packages and the sample's own projects
