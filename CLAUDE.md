# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Code-change gate (read this first)

**Do not modify code unless the change is backed by one of two things:**

1. **An OpenSpec change** — an active `openspec/changes/<name>/` whose tasks/specs cover the edit. New feature work, refactors, behavior changes, and infrastructure/pipeline changes all go through OpenSpec. If no change exists yet, scaffold one with `/opsx:propose` (and branch per "Implementing an OpenSpec change" below) **before** touching source.
2. **A bug fix** — correcting observed-incorrect behavior versus intended: a failing test, a runtime exception, a broken deploy, a security flaw, or output that contradicts a stated spec/README/intent. Bug fixes follow the existing `fix/<name>` convention. When you claim "this is a bug," state the symptom (what's wrong vs. what's expected) so it's verifiable.

If a request would change code but is neither spec-backed nor a clear bug, **stop and surface that** — offer to open an OpenSpec change (`/opsx:propose`) or ask the user to confirm it's a bug fix. Do not silently start editing source.

**What counts as "code" under this gate:** anything that changes the running app or the build/deploy pipeline — `src/**`, EF migrations, `infrastructure/**` (CloudFormation/SAM templates), `.github/workflows/**`, `Dockerfile`, and CI/deploy scripts under `scripts/**`.

**What is exempt** (edit freely on the user's request, no spec/bug needed): documentation and Markdown, the OpenSpec artifacts themselves, the brothers tooling and developer-experience config (`CLAUDE.md`, `.claude/**`, `BROTHERS.md`, the status line, `.brother-status`, local-only `scripts/**` that don't run in CI/deploy), comments, and formatting. When in doubt about whether a `scripts/**` file is pipeline-bound, treat it as code and ask.

## Stack reality vs. README

The root `README.md` is partially out of date — trust the code over the README:

- **Framework**: `net10.0` (not net8). All projects target it. CI uses `dotnet-version: 10.0.x`.
- **Database**: MySQL via `Pomelo.EntityFrameworkCore.MySql` (not PostgreSQL). Production runs Aurora MySQL Serverless v2 (Global Cluster on `app`/`beta`/`alpha`). README mentions PostgreSQL/RDS — ignore.
- **Hosting**: Containerized — Dockerfile at [src/Tjb.Web/Dockerfile](src/Tjb.Web/Dockerfile) builds a `mcr.microsoft.com/dotnet/aspnet:10.0` image pushed to ECR and run behind an ALB. The README's "AWS Lambda + API Gateway" description is stale; only `Tjb.Api` retains Lambda packaging code (`Amazon.Lambda.AspNetCoreServer`) but the deployed surface is the Web container. Treat `Tjb.Api` as a vestigial/secondary project — `Tjb.Web` is the live application.
- **Auth**: All real authentication lives in `Tjb.Web` (ASP.NET Identity + Google OAuth). [src/Tjb.Api/Controllers/AuthController.cs](src/Tjb.Api/Controllers/AuthController.cs) is intentionally a no-op ("authentication disabled") — don't try to "fix" it.

## Common commands

```powershell
# Build / test (run from repo root)
dotnet restore
dotnet build --configuration Release
dotnet test --filter "FullyQualifiedName!~Tjb.UiTests"   # CI runs unit tests this way; UI tests are excluded pre-deploy

# Run the web app locally (Blazor Server + Identity + Google OAuth)
dotnet run --project src/Tjb.Web

# Run the API locally (mostly health endpoint + Swagger)
dotnet run --project src/Tjb.Api

# EF Core migrations — migrations live in their OWN project, not in Data
dotnet ef migrations add <Name> --project src/Tjb.Migrations --startup-project src/Tjb.Migrations
dotnet ef database update           --project src/Tjb.Migrations --startup-project src/Tjb.Migrations

# Apply migrations + seed (standalone runner)
dotnet run --project src/Tjb.Migrations

# UI tests (Playwright/xUnit) — point at a deployed env via TEST_BASE_URL
cd src/Tjb.UiTests
dotnet build                                # also restores Playwright browsers
dotnet test                                 # runs against BaseUrl in appsettings.json (default: https://dev.appcloud.systems)
dotnet test --filter "FullyQualifiedName~LoginNavigation"   # single test

# Inspect the deployed Aurora MySQL DB: AWS Console -> RDS -> Query Editor
# (authenticate with the DB credentials secret in Secrets Manager). There is no
# local tunnel/bastion helper — the prior Connect-AuroraDB.ps1 was removed as dead.
```

**Reading test results in CI:** the deploy workflow ([.github/workflows/zbuild.yml](.github/workflows/zbuild.yml)) produces a first-class test summary on the run page via `dorny/test-reporter@v1` (TRX → markdown summary + check-run annotations), plus uploaded artifacts. Start debugging a red run at the **run-page summary** (the `Unit tests` / `UI tests` report and the `ui-test-artifacts-*` artifact containing Playwright `trace.zip`), not by scrolling the raw step logs. TRX filenames are pinned (`unit-tests.trx`, `ui-tests.trx`); artifact retention is 7 days (30 on `app`).

## Architecture

Nine projects in [Tjb.sln](Tjb.sln) (plus `Tjb.Data.Test`):

- **Tjb.Shared** — DTOs and enums (`TaskStatus`, `TaskPriority`, `ProjectRole`). Packed as a NuGet on every CI build.
- **Tjb.Data** — EF Core `DbContext`, entity classes, configurations. `TjbDbContext` now derives **`AwsWebAppIdentityDbContext`** (from `Tjb.Web.Framework.Data`) rather than `IdentityDbContext<IdentityUser>` directly, so the Identity tables come from the reusable framework base while the task entities stay here. The DbContext is wired via `mysqlOptions.MigrationsAssembly("Tjb.Migrations")` — migrations are NOT generated into this project.
- **Tjb.Migrations** — Holds EF migration files, an `IDesignTimeDbContextFactory` (so `dotnet ef` can resolve a connection string from its own `appsettings.json`), and a standalone `Program.cs` that applies migrations + seeds initial data. This is also referenced by `Api` and `Web` so they can apply migrations on startup.
- **Tjb.Web.Framework.Data** — Tiny `Microsoft.NET.Sdk` library holding **`AwsWebAppIdentityDbContext : IdentityDbContext<IdentityUser>`**, the Identity-only base context a consumer derives. References only `Microsoft.AspNetCore.Identity.EntityFrameworkCore` + EF Core (no Pomelo, no task entities). Packed as NuGet `Tjb.Web.Framework.Data`.
- **Tjb.Web.Framework** — Razor Class Library (`Microsoft.NET.Sdk.Razor`) holding the **reusable web surface**: the `Areas/Identity` pages (incl. the SES-sending `ExternalLogin`), the `Shared/` layout+shell (`MainLayout`/`NavMenu`/`LoginDisplay`/`SurveyPrompt`), `Pages/Error` + `Pages/EmailTemplates`, the `Services/` SES email + view-render, the `RevalidatingIdentityAuthenticationStateProvider`, and `wwwroot` static assets (served from `_content/Tjb.Web.Framework/`). Moved code keeps its `Tjb.Web.*` namespaces (RootNamespace pinned to `Tjb.Web`). Packed as NuGet `Tjb.Web.Framework`.
- **Tjb.Web.Hosting** — `Microsoft.NET.Sdk` library of `IServiceCollection`/`IApplicationBuilder` extension methods (namespace `Microsoft.Extensions.DependencyInjection`): `AddAwsWebAppIdentity<TContext>`, `AddAwsWebAppGoogleAuth<TContext>`, `AddAwsWebAppEmail`, `UseAwsWebAppForwardedHeaders`, `ApplyDatabaseMigrationsAsync<TContext>`, and `LogAwsWebAppAuthConfig`. A host `Program.cs` shrinks to: register its own `DbContext`, call these, map its own pages. Packed as NuGet `Tjb.Web.Hosting`.
- **Tjb.Api** — Minimal Web API. Currently exposes only `/health`, Swagger (in dev), and stub `AuthController` endpoints. Still contains Lambda hosting glue (`LambdaEntryPoint`, `Startup`) but is not the deployed front door.
- **Tjb.Web** — **The deployed application, and the first consumer of the framework packages.** Blazor Server + Razor Pages + ASP.NET Identity + Google OAuth, now built from `Tjb.Web.Framework` + `Tjb.Web.Hosting` (it keeps only its own pages — `Index`/`Counter`/`FetchData` — `WeatherForecastService`, `App.razor`, and `Pages/_Host.cshtml`). On startup it calls `EnsureCreatedAsync()` then `MigrateAsync()` (via `ApplyDatabaseMigrationsAsync`). Sits behind an ALB so it configures `ForwardedHeaders` (`X-Forwarded-Proto`/`-For`) with `KnownProxies`/`KnownNetworks` cleared (via `UseAwsWebAppForwardedHeaders`) — needed for the Google OAuth `/signin-google` callback to see HTTPS. `App.razor`/`_Host.cshtml` stay in the host because the Blazor `Router` discovers the host's own pages by assembly.
- **Tjb.UiTests** — Playwright + xUnit. Runs against a *deployed* URL, not a local server. Uses token-based Google auth in CI (`GOOGLE_TEST_ACCESS_TOKEN`/`REFRESH_TOKEN`) rather than scripting the OAuth UI.

The reusable web tier (`Tjb.Web.Framework` + `Tjb.Web.Hosting` + `Tjb.Web.Framework.Data`) is the **`extract-web-framework-package`** OpenSpec change — the first of a roadmap (`extract-web-framework-package` → `sample-solution-local` → `sample-ci-deploy` → `framework-slipstream-upgrade`) toward spinning up multiple multi-regional web apps from one framework. `Tjb.Web` currently references the three via `ProjectReference`; the change's phase 4 swaps to `PackageReference` once CI publishes them.

Both `Web` and `Api` apply migrations on startup but **swallow exceptions** so the app still boots if migrations fail (intentional, to avoid Lambda/cold-start crashes). Don't change this to throw without thinking through the deployment story.

## Infrastructure naming convention

Resource names across CloudFormation templates and the deploy workflow are **derived, not hardcoded**. The single source of truth is the deployment domain (`secrets.DOMAIN_NAME`, e.g. `appcloud.systems`), with a "dashed" form (`.` → `-`, e.g. `appcloud-systems`) used everywhere AWS naming forbids dots.

**Bootstrap stack** ([infrastructure/bootstrap.template](infrastructure/bootstrap.template), one per region):
- **Stack name = dashed domain.** No `bootstrap-` prefix. For `appcloud.systems` the stack is named `appcloud-systems` in every region it's deployed to.
- All resources it owns derive their names from `!Ref AWS::StackName` (= the dashed domain). Examples for the `appcloud-systems` deploy:
  - KMS aliases: `alias/appcloud-systems-aurora-{prod,nonprod}`
  - SSM parameters: `/appcloud-systems/kms/{prod,nonprod}/aurora-key-arn`
  - IAM users: `appcloud-systems-GitHubActionsUser`, `appcloud-systems-GitHubActionsUserProd`
  - IAM role: `appcloud-systems-prod-kms-admin`
  - ECR repository: `appcloud-systems`
  - S3 templates bucket: `${AWS::AccountId}-${AWS::StackName}-${AWS::Region}` (e.g. `991795635857-appcloud-systems-us-east-1`)

**Env stacks** (`master.template` → nested `backend.template` + `application.template` and their children):
- **Stack name = `{branch-leaf}-{dashed-domain}`** (e.g. `dev-appcloud-systems`, `app-appcloud-systems`).
- Templates take a `DomainName` parameter in **dot form** (`appcloud.systems`). The workflow passes `${{ secrets.DOMAIN_NAME }}` directly.
- Templates that need the dashed or underscored form **derive locally** via CFN intrinsics (no second parameter):
  ```yaml
  # dashed (for SSM paths, secret names, IAM scopes, Aurora cluster IDs):
  !Sub
    - "${DomainDashed}-${BranchName}-global-cluster"
    - DomainDashed: !Join ["-", !Split [".", !Ref DomainName]]

  # underscored (for MySQL master username — MySQL disallows hyphens):
  !Sub
    - "${DomainUnderscored}_admin"
    - DomainUnderscored: !Join ["_", !Split [".", !Ref DomainName]]
  ```

**Deploy workflow** ([.github/workflows/zbuild.yml](.github/workflows/zbuild.yml)):
- AWS regions are **GitHub repo Variables** (Settings → Secrets and variables → Actions → Variables tab):
  - `AWS_REGION_PRIMARY` (currently `us-east-1`)
  - `AWS_REGION_SECONDARY` (currently `us-east-2`)
- The workflow has no fallback if these are unset — empty values fail the matrix.
- Dashed-domain is computed once per job in the `Process Domain Name` step as `steps.process-domain.outputs.processed-domain` and reused for: bootstrap stack name in SSM lookup paths, ECR image URIs, templates-bucket name, env-stack `--parameter-overrides DomainName=...`.
- Zero hardcoded references to specific domains (`appcloud-systems`, `taskmanager`) or specific bucket/repo patterns remain in the workflow.

**Why this matters when editing templates or the workflow:** don't reintroduce hardcoded project names or region literals. The whole pipeline is domain-and-region-agnostic; reintroducing a literal anywhere breaks that property silently until someone tries to deploy a second domain or change a region.

## Resource tagging (tag-cloudformation-resources)

Every deploy applies six **stack-level tags** via the `tags:` input on the `Deploy template` step (`aws-actions/aws-cloudformation-github-deploy`), computed in the `Compute resource tags` step. CloudFormation auto-propagates them to every taggable resource, including those in nested stacks (`master`→`backend`/`application`→children); the ECS web service additionally sets `PropagateTags: SERVICE` so running tasks inherit them. No per-resource `Tags:` blocks.

| Tag | Value source |
| --- | --- |
| `Stack Name` | `${branch-leaf}-${processed-domain}` |
| `Create Date` | existing stack's tag if present (preserved across redeploys), else `date -u +%F` on first create |
| `Branch` | branch leaf |
| `Specification` | branch leaf (= OpenSpec change name for feature branches); `shared-infrastructure` on `app`/`beta`/`alpha`/`dev` |
| `Version` | `build.customVersion` (SemVer) |
| `DeployRunUrl` | the GitHub Actions run URL |

Note the two keys with spaces (`Stack Name`, `Create Date`) — keep them literal. **Cost-allocation activation is separate:** tags existing ≠ cost-allocation tags enabled; that's a manual, account-level step in the Billing console.

## Branch model & CI/CD ([BRANCH_MANAGEMENT_README.md](BRANCH_MANAGEMENT_README.md), [.github/workflows/zbuild.yml](.github/workflows/zbuild.yml))

This repo has an unusual branching scheme — read carefully before doing anything git-related:

- **Solo developer — there are NO pull requests.** This repo is maintained by a single developer; changes are integrated by **direct merge/push**, never via GitHub PRs or code review. Do not offer to "open a PR", wait for review, or describe work as blocked on a merge request — just merge/push directly when asked.
- **`app` is the production branch** (and the GitVersion `main`). Feature/`{type}/{name}` branches merge down into `dev` for integration and up into `app` to release — by hand, not through PRs.
- Three "shared infrastructure" branches deploy multi-region (`AWS_REGION_PRIMARY` + `AWS_REGION_SECONDARY` repo vars; currently `us-east-1` + `us-east-2`): `app`, `beta`, `alpha`. They use `infrastructure/master.template` (full backend incl. Aurora Global Cluster).
- `dev` is single-region but also uses the master template.
- Every other branch follows `{type}/{name}` where type is `build|deploy|system|feature|fix`. These deploy `infrastructure/application.template` (just the app stack, importing backend exports from `dev`) into a per-branch CloudFormation stack named `{branch-leaf}-{processed-domain}`.
- Every push triggers `Deploy Everything` workflow → builds, tests, builds Docker image, deploys via SAM/CloudFormation, runs UI tests against the deployed URL `https://{branch-leaf}.{DOMAIN_NAME}`, then publishes NuGets to GitHub Packages.

Useful workflow controls:
- Add `nodeploy` to a non-merge commit message to skip the deploy + UI test + publish jobs.
- A new push to any **non-`app`** branch cancels the in-flight workflow run for that branch (build, test, deploy, and UI tests all stop) — "latest push wins". This comes from the workflow-level `concurrency: { group: workflow-${{ github.ref }}, cancel-in-progress: ${{ github.ref != 'refs/heads/app' }} }` block. `app` is exempt: pushes there **queue** behind the in-flight run so production deploys are never interrupted mid-flight. The `deploy` job keeps its own job-level `concurrency` block (`deploy-{region}-{branch}`, `cancel-in-progress: false`) for the cleanup-vs-deploy mutex.
- The `dev`/`alpha`/`beta`/`app` branches **fail the build if `changes/` is non-empty** — use `scripts/merge-to-dev.ps1` (or the `.sh` variant) to flush pending change files into `changelog.md`.
- New branches should be created with `scripts/create-branch.ps1`, which generates a `changes.md` whose first non-empty line becomes the changelog entry on merge into `dev`.
- The Docker image is content-addressed by a SHA256 of `src/`; if an image with that tag already exists in ECR, the build step is skipped.

## Email (AWS SES)

Confirmation emails sent after Google OAuth registration are delivered via AWS SES. The flow lives in [src/Tjb.Web/Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs](src/Tjb.Web/Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs); rendering is in [src/Tjb.Web/Services/](src/Tjb.Web/Services/) (`IEmailService` → `AwsSesEmailService`, `IViewRenderService`), template in [src/Tjb.Web/Pages/EmailTemplates/ConfirmationEmail.cshtml](src/Tjb.Web/Pages/EmailTemplates/ConfirmationEmail.cshtml).

**Config** (read via `IOptions<AwsSesOptions>`, section `AwsSes`):
- `AwsSes:Region` — leave empty in deployed envs (AWS SDK auto-detects from Fargate metadata). Set explicitly only for local dev.
- `AwsSes:SenderEmail` — must be a verified SES identity. Hardcoded to `noreply@appcloud.systems` in [infrastructure/web.template](infrastructure/web.template) (container env var). Override locally via `dotnet user-secrets set "AwsSes:SenderEmail" "..."`.

**IAM**: ECS tasks assume `SharedLambdaExecutionRole` from [infrastructure/security.template](infrastructure/security.template) which now includes a `SesAccess` policy granting `ses:SendEmail` / `ses:SendRawEmail`. Adding any other AWS SDK call from the container requires extending this role.

**Per-region SES setup** (each region is independent):
1. **Domain identity**: `aws sesv2 create-email-identity --email-identity appcloud.systems --region <region>` then add the 3 returned DKIM CNAMEs to Route53 hosted zone `Z06422172SASV44F5Y8VA`. Verify with `aws sesv2 get-email-identity --email-identity appcloud.systems --region <region>` (look for `DkimAttributes.Status: SUCCESS`).
2. **Sandbox vs production**: new SES accounts start in sandbox (200 emails/day, recipients must also be verified). To exit, run `aws sesv2 put-account-details --production-access-enabled --mail-type TRANSACTIONAL --website-url https://appcloud.systems --use-case-description "<description>" --additional-contact-email-addresses <email> --contact-language EN --region <region>`. AWS reviews within ~24h.
3. **Test recipients in sandbox**: each address you want to send TO must be verified via `aws ses verify-email-identity --email-address <addr> --region <region>` until production access is granted.

**Currently verified** (both regions): `appcloud.systems` (domain, DKIM), `hounddog@gmail.com` (test address, sandbox-era).

## Implementing an OpenSpec change

When starting work on a spec — anything under `openspec/changes/<name>/` or `openspec/specs/<name>/` — **branch from `dev` with the spec's exact name** (no `feature/`/`fix/`/etc. type prefix):

```powershell
git checkout dev
git pull
git checkout -b <spec-name>
```

For example, to begin implementing the in-flight change `robust-branch-stack-cleanup`, work on a branch named literally `robust-branch-stack-cleanup`. The branch-leaf computation in the deploy workflow (`BRANCH_NAME=${FULL_BRANCH_NAME##*/}`) strips any path prefix, so the resulting per-branch CloudFormation stack is `<spec-name>-<dashed-domain>` (e.g. `robust-branch-stack-cleanup-appcloud-systems`) deployed to `https://<spec-name>.{DOMAIN_NAME}`. The bare-name convention is for human readability — branch → CloudFormation stack → deployed URL all carry the spec name verbatim (there are no PRs; see the solo-developer note under "Branch model & CI/CD").

This rule applies to:
- A change being newly implemented (`openspec/changes/<name>/`).
- A revisit / modification of a capability already in `openspec/specs/<name>/`.

It does **not** apply to:
- Operational fixes or small chores unrelated to a spec — those follow the existing `{type}/{name}` convention.
- The four shared-infrastructure branches (`app`/`beta`/`alpha`/`dev`) which serve their own purposes.

Multiple specs in flight at once don't collide because each spec name is unique. If the user has scaffolded a change with `/opsx:propose` but hasn't yet branched, do that as the first step of implementation — before touching any source file.

## Parallel work folders (the brothers)

This repo is worked from **multiple sibling folders at once** so several features can be in flight in parallel. Every folder is a **`git worktree` of one shared bare repo** (`.bare`) under the **family directory** (the parent of this repo root, e.g. `…\awswebapp\`). The shared object store is what makes cross-folder merges local and instant (from `dev`: `git merge <brother-branch>`, no push/pull). See [BROTHERS.md](BROTHERS.md) for the full convention.

- Each work folder is a **brother** in a family of Germans; the folder's leaf name **is** the brother's identity — one of the names on the roster in [BROTHERS.md](BROTHERS.md) (`claude` is a brother too: the eldest/default — don't exclude it as "not German").
- `app`, `beta`, `alpha`, and `dev` are not brothers — they are the **homestead** (shared long-lived worktrees); `/brothers` lists them separately. `.bare` is the hub, not a folder you work in.
- **A branch lives in only one worktree at a time** — so each homestead branch gets its own folder and a brother is never put *on* `dev` (he's parked detached at dev's tip until assigned a branch).
- A brother works one branch at a time: the **brother (folder) = who**, the **branch = what**. They're independent — `wilhelm` might be on branch `cancel-superseded-runs`.

**The family questions** are answered the same way whether asked as a slash command **or in plain conversation** ("who are you?", "what's the rest of the family up to?", "you have a new brother", "what should I do next?"). Each is backed by one script under `scripts/` — run it and narrate the result; don't re-derive the git plumbing here. The detailed procedure for each lives in its command file and in [BROTHERS.md](BROTHERS.md), and the executable logic is shared via `scripts/_BrothersCommon.ps1` (so `/brothers` and `/next` enumerate siblings the same way).

| Ask | Command | Helper | What it does |
|---|---|---|---|
| Who am I / who are you | `/whoami` | (inline git) | Leaf of `git rev-parse --show-toplevel` = your brother name; report branch, last commit, tree, `.brother-status`. Leaf `dev`/`app` ⇒ homestead, not a feature brother. |
| What are my brothers doing | `/brothers` | `scripts\Get-Brothers.ps1` | One line per sibling: branch, tree state, ahead/behind, last commit, note. |
| Welcome a new brother | `/newbrother` | `scripts\New-Brother.ps1` | Picks the next roster name (or `-Name`), creates the folder as a worktree, parks him **detached at `dev`'s tip** (or a branch off dev with `-Branch`). Never puts him *on* the `dev` branch — git allows a branch in only one worktree, so that would block every folder from checking out `dev`. |
| What should I do next | `/next` | `scripts\Get-NextStep.ps1` | On a spec ⇒ progress + next unchecked task/step. Idle ⇒ suggests an OpenSpec change **no brother's branch is on**, so the family doesn't double up. |

All are read-only except `/newbrother` (creates the worktree). Never modify anything to *answer* an identity/next question.

## Things that will trip you up

- **Don't add migrations to `Tjb.Data`** — the `MigrationsAssembly` is `Tjb.Migrations`. EF tooling needs `--project src/Tjb.Migrations`.
- **`Tjb.Api` is mostly inert.** Adding endpoints there won't reach users; add them to `Tjb.Web`.
- **Connection strings in `appsettings.json` are localhost defaults** (`Server=localhost;...root/password`). Real values come from environment / Secrets Manager in deployed envs and from `dotnet user-secrets` locally.
- **Forwarded headers config in [src/Tjb.Web/Program.cs](src/Tjb.Web/Program.cs) clears `KnownProxies`/`KnownNetworks` on purpose** — the ALB has dynamic IPs. Don't tighten it without verifying OAuth still works.
- **NuGet package versions are branch-suffixed** for non-`app` branches (`{version}-{sanitized-branch}`) so consumers can pin to a feature branch's build.
- **Deleting a remote branch tears down its CloudFormation stack.** [.github/workflows/cleanup-on-branch-delete.yml](.github/workflows/cleanup-on-branch-delete.yml) fires on the GitHub `delete` event, computes the same `{branch-leaf}-{processed-domain}` stack name the deploy job uses, and calls `aws cloudformation delete-stack` in `us-east-1`. `app`, `beta`, `alpha`, and `dev` are hard-coded as protected — the workflow no-ops for any branch whose leaf segment matches one of those (including e.g. `feature/dev`). It also clears `s3://${AccountId}-${DashedDomain}-us-east-1/{branch-leaf}/` (the bootstrap-owned templates bucket — see Infrastructure naming convention above) after `DELETE_COMPLETE`. **Pre-cleanup (robust-branch-stack-cleanup):** before `delete-stack`, the workflow now enumerates the full stack tree (recursing nested stacks via `list-stack-resources`) and preemptively empties every stack-owned S3 bucket, deletes every stack-owned ECR repo's images, and force-tears-down every stack-owned Aurora cluster (delete members → clear deletion-protection → `delete-db-cluster --skip-final-snapshot` → wait). It acts ONLY on CFN-tracked members of the stack being deleted — the bootstrap's `TemplatesBucket`/`WebECRRepository`/KMS keys are never touched — and halts before `delete-stack` if any pre-cleanup step fails, so operators no longer hand-clean these before retrying a stuck delete. **GitHub Actions constraint — default-branch-only workflows (gotcha):** GitHub dispatches repo-level events like `delete` **and `schedule`** using the workflow file *as it exists on the default branch* (`app`) — never the copy on `dev` or a feature branch. So edits to `cleanup-on-branch-delete.yml` (or any `delete`/`schedule`-triggered workflow) are integrated but **dormant** until a deliberate `dev`→`app` promotion; e.g. the `robust-branch-stack-cleanup` pre-cleanup logic above does not actually run until it reaches `app`. The `default-branch-workflow-promotion` change adds a CI drift guard that flags, on each non-`app` run, any such workflow that differs from `app` (a "dormant until promoted" notice on the run summary).
