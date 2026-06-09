# Tjb — Task & Project Management Web Application

A full-stack task and project management web application built with C# / ASP.NET on **.NET 10**, deployed to AWS as a Docker container on **ECS Fargate** behind an Application Load Balancer. Authentication is ASP.NET Identity + Google OAuth; the database is **Aurora MySQL Serverless v2**.

## Architecture Overview

- **Framework**: .NET 10 (`net10.0`) across all projects.
- **Application**: `Tjb.Web` — Blazor Server + Razor Pages + ASP.NET Identity + Google OAuth. This is the deployed front door.
- **Database**: Aurora MySQL Serverless v2 (MySQL 8.0, port 3306) via `Pomelo.EntityFrameworkCore.MySql`. Connections use `UseMySql(...)` with `ServerVersion.AutoDetect`. (A stale `Npgsql` package reference lingers in `Tjb.Data.csproj` but is unused — there is no PostgreSQL.)
- **Authentication**: ASP.NET Identity with Google OAuth, entirely within `Tjb.Web`. There is no JWT and no separate auth service. `Tjb.Api`'s `AuthController` is an intentional no-op.
- **Hosting**: The `Tjb.Web` container image (built from [src/Tjb.Web/Dockerfile](src/Tjb.Web/Dockerfile), `mcr.microsoft.com/dotnet/aspnet:10.0`) is pushed to ECR and run on ECS Fargate behind an ALB. `Tjb.Web` configures `ForwardedHeaders` so the Google OAuth `/signin-google` callback sees HTTPS through the ALB.
- **Infrastructure**: AWS CloudFormation/SAM templates under [infrastructure/](infrastructure/) (ECR, ECS/Fargate, ALB, Aurora, networking, SES). Resource names are derived from the deployment domain — see [CLAUDE.md](CLAUDE.md) for the naming convention.
- **Email**: Confirmation emails after Google OAuth registration are sent via AWS SES from `Tjb.Web`.

## Project Structure

Six projects in [Tjb.sln](Tjb.sln) (plus a test project):

> **Note:** The reusable web surface now ships as NuGet packages. The Identity UI, Blazor layout/shell,
> SES email services, auth-state provider, and static assets live in **`Tjb.Web.Framework`** (a Razor Class
> Library); the startup wiring (Identity, Google OAuth, SES, ALB forwarded-headers, migrate-on-startup) lives
> in **`Tjb.Web.Hosting`** (extension methods); and the Identity-only `DbContext` base
> (`AwsWebAppIdentityDbContext`) lives in **`Tjb.Web.Framework.Data`**. `Tjb.Web` is the first consumer of
> these packages — a host app provides only configuration and its own pages. This is the
> `extract-web-framework-package` change, the first step toward standing up additional web apps from one
> framework (roadmap: `extract-web-framework-package` → `sample-solution-local` → `sample-ci-deploy` →
> `framework-slipstream-upgrade`). The package names/layout below are stale (`TaskManager.*` → `Tjb.*`).

```
src/
├── Tjb.Shared/        # DTOs and enums (TaskStatus, TaskPriority, ProjectRole); packed as a NuGet on each build
├── Tjb.Data/          # EF Core DbContext (TjbDbContext : IdentityDbContext), entities, configurations
├── Tjb.Migrations/    # EF migration files, IDesignTimeDbContextFactory, and a standalone migrate+seed Program.cs
├── Tjb.Api/           # Minimal Web API — only /health, Swagger (dev), and a stub no-op AuthController.
│                      #   Retains vestigial Lambda hosting glue but is NOT the deployed surface.
├── Tjb.Web/           # THE DEPLOYED APP — Blazor Server + Razor Pages + Identity + Google OAuth
└── Tjb.UiTests/       # Playwright + xUnit, run against a deployed URL (not a local server)
```

`Tjb.Data.Test` holds unit tests for the data layer.

> Migrations live in **`Tjb.Migrations`**, not `Tjb.Data`. `TjbDbContext` is wired with `MigrationsAssembly("Tjb.Migrations")`, so EF tooling must target that project.

## Quick Start

### Prerequisites
- .NET 10 SDK
- A local MySQL 8.0 server (for local development)
- Google Cloud Console OAuth credentials (Client ID/Secret)
- AWS CLI configured (for deployment / DB access)

### Local Development

1. **Clone and restore**
   ```powershell
   git clone <repository-url>
   dotnet restore
   ```

2. **Configure local secrets** (connection string defaults to localhost MySQL; override OAuth via user-secrets)
   ```powershell
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=TjbDb;User=root;Password=password;" --project src/Tjb.Web
   dotnet user-secrets set "Authentication:Google:ClientId" "your-google-client-id" --project src/Tjb.Web
   dotnet user-secrets set "Authentication:Google:ClientSecret" "your-google-client-secret" --project src/Tjb.Web
   ```
   The default connection string in `appsettings.json` is `Server=localhost;Database=TjbDb;User=root;Password=password;`. Real values come from the environment / Secrets Manager in deployed environments.

3. **Apply migrations** (migrations live in `Tjb.Migrations`)
   ```powershell
   dotnet ef database update --project src/Tjb.Migrations --startup-project src/Tjb.Migrations
   # or run the standalone migrate + seed runner:
   dotnet run --project src/Tjb.Migrations
   ```

4. **Run the web app**
   ```powershell
   dotnet run --project src/Tjb.Web
   ```

`Tjb.Web` calls `EnsureCreatedAsync()` then `MigrateAsync()` on startup; migration exceptions are swallowed so the app still boots.

### Build & Test

```powershell
dotnet build --configuration Release
dotnet test --filter "FullyQualifiedName!~Tjb.UiTests"   # CI runs unit tests this way; UI tests are excluded pre-deploy
```

### EF Core Migrations

```powershell
dotnet ef migrations add <Name> --project src/Tjb.Migrations --startup-project src/Tjb.Migrations
dotnet ef database update        --project src/Tjb.Migrations --startup-project src/Tjb.Migrations
```

## Database Schema

Core entities (see `Tjb.Data`):

- **Projects** — owned by a user; has members.
- **Tasks** — belong to a project, optionally assigned to a user; carry a `TaskStatus` and `TaskPriority`.
- **ProjectMembers** — link users to projects with a `ProjectRole`.
- **Identity tables** — `TjbDbContext` extends `IdentityDbContext<IdentityUser>`, so ASP.NET Identity tables share the same database.

Shared enums (`TaskStatus`, `TaskPriority`, `ProjectRole`) live in `Tjb.Shared`.

## Branch Model & CI/CD

This repo is maintained by a **solo developer — there are no pull requests**; changes are integrated by direct merge/push.

- **`app`** is the production branch (GitVersion `main`). **`dev`** is integration.
- `app`, `beta`, and `alpha` are shared multi-region infrastructure branches using `infrastructure/master.template`. `dev` is single-region but also uses the master template.
- Every other branch follows `{type}/{name}` (`build|deploy|system|feature|fix`) and deploys `infrastructure/application.template` into a per-branch stack.
- Every push runs the **Deploy Everything** workflow ([.github/workflows/zbuild.yml](.github/workflows/zbuild.yml)): build → test → Docker image → ECR → CloudFormation/SAM deploy → UI tests against the deployed URL → publish NuGets.

For each branch, the deployed URL is `https://{branch-leaf}.{DOMAIN_NAME}` (e.g. `https://dev.appcloud.systems`) and the CloudFormation stack is `{branch-leaf}-{dashed-domain}` (e.g. `dev-appcloud-systems`).

See [BRANCH_MANAGEMENT_README.md](BRANCH_MANAGEMENT_README.md) for the branch-management scripts and changelog workflow, and [CLAUDE.md](CLAUDE.md) for the full infrastructure naming convention and deploy details.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
