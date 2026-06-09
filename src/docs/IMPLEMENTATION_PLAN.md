# Implementation State

> Historical note: this file once held a from-scratch build plan (a .NET 8 + AWS
> Lambda + PostgreSQL design). That system has been built, and the stack has since
> diverged from that plan. This document now records the **current** implementation
> state. For deeper architecture detail see [ARCHITECTURE.md](ARCHITECTURE.md).

## Status

The application is built and deployed. New work is tracked through OpenSpec changes
(`openspec/changes/<name>/`), not against this plan.

## Stack as implemented

- **Framework**: `net10.0` across all projects (CI uses `dotnet-version: 10.0.x`).
- **Database**: Aurora MySQL Serverless v2 via `Pomelo.EntityFrameworkCore.MySql`
  (`UseMySql`, port 3306). The two multi-region branches (`app`, `test`) run an
  Aurora Global Cluster.
  > A stale `Npgsql.EntityFrameworkCore.PostgreSQL` package reference remains in
  > `Tjb.Data.csproj` but is unused — the runtime path is `UseMySql`.
- **Hosting**: `Tjb.Web` is built into a Docker image
  ([src/Tjb.Web/Dockerfile](../Tjb.Web/Dockerfile),
  `mcr.microsoft.com/dotnet/aspnet:10.0`), pushed to ECR, and run on **ECS Fargate
  behind an Application Load Balancer**. This is the deployed surface. (Earlier plans
  targeted Lambda + API Gateway; that approach was abandoned.)
- **Auth**: ASP.NET Identity + Google OAuth, all in `Tjb.Web`.

## Projects (six, in [Tjb.sln](../../Tjb.sln))

| Project | Role |
|---|---|
| `Tjb.Shared` | DTOs and enums (`TaskStatus`, `TaskPriority`, `ProjectRole`); packed as a NuGet on each CI build. |
| `Tjb.Data` | EF Core `TjbDbContext` (extends `IdentityDbContext<IdentityUser>`), entities, configurations. `MigrationsAssembly` is `Tjb.Migrations`. |
| `Tjb.Migrations` | Holds the migration files, a design-time `IDesignTimeDbContextFactory`, and a standalone `Program.cs` that applies migrations + seeds an admin. |
| `Tjb.Api` | Minimal Web API (`/health`, Swagger in dev, stub `AuthController`). Vestigial/secondary — still carries Lambda packaging glue but is **not** the deployed front door. |
| `Tjb.Web` | **The deployed application** — Blazor Server + Razor Pages + Identity + Google OAuth. |
| `Tjb.UiTests` | Playwright + xUnit, run against a *deployed* URL. |

## Data model

The schema (see [src/Tjb.Migrations/20251007185200_InitialCreate.cs](../Tjb.Migrations/20251007185200_InitialCreate.cs))
covers `Users`, `Projects`, `Tasks`, `ProjectMembers`, and `Invitations`, alongside
the ASP.NET Identity `AspNet*` tables (Identity shares the same database).

## Where to go next

- Architecture and data model: [ARCHITECTURE.md](ARCHITECTURE.md)
- Database access for developers: [DATABASE_ACCESS_GUIDE.md](DATABASE_ACCESS_GUIDE.md)
- Migrations workflow: [DATABASE_MIGRATIONS_GUIDE.md](DATABASE_MIGRATIONS_GUIDE.md)
- Deploy pipeline: [../../.github/workflows/zbuild.yml](../../.github/workflows/zbuild.yml)
- Branch model and CI/CD: [../../BRANCH_MANAGEMENT_README.md](../../BRANCH_MANAGEMENT_README.md)
