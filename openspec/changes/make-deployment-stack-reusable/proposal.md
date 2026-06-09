## Why

The infrastructure pattern this repo has converged on — multi-region Aurora Serverless v2 + ECS Fargate behind an ALB, branch-per-stack deploys, the consolidated `bootstrap` stack with prod/nonprod KMS isolation, the cleanup-on-branch-delete workflow, the branch-conditional CI credentials — is generic enough to deploy other .NET web apps with almost no changes besides naming. Today it can't actually be reused without edits: the templates default `DomainName` to `appcloud.systems`, the workflow knows about `Tjb.Web` and a specific source-tree layout, and there's no extracted `ProjectName` parameter (resource names already derive from `DomainName`/`${AWS::StackName}`, so the old `taskmanager-*` literal is no longer the blocker). The same pattern gets re-implemented (poorly, partially) every time a new .NET web app needs to land on AWS.

Bundle the templates and reusable workflow pieces into something a new .NET project can pull in by reference, and the next new app's deploy story shrinks from "fork this repo and hope you find all the hardcoded strings" to "install a NuGet package and call a reusable workflow."

> **Baseline note (kept in sync with the codebase):** the older `taskmanager-*` resource-name hardcoding this proposal originally targeted has since been eliminated by the archived `domain-named-bootstrap-stack` / `domain-derived-resource-naming` work — resource names now derive from `DomainName` (dot form, default `appcloud.systems`) and `${AWS::StackName}`, and a CI grep guard fails the build if `taskmanager` reappears in any template or `zbuild.yml`. The remaining reusability gap is therefore the `appcloud.systems`/`Tjb.Web` specifics and the absence of a dedicated `ProjectName` parameter — not `taskmanager` literals.

## What Changes

- Extract the project-agnostic CloudFormation templates ([infrastructure/](../../../infrastructure/) — `bootstrap.template`, `master.template`, `backend.template`, `db.template`, `network.template`, `infrastructure.template`, `web.template`, `api.template`, `dns.template`, plus a few likely-new ones) into a new NuGet package, **`YadaYada.AwsWebApp.DeploymentStack`** (final name TBD in tasks). The package is published to **GitHub Packages** on this repo's same NuGet feed (`nuget.pkg.github.com/${owner}/index.json`), alongside the existing `Tjb.Shared` / `Tjb.Data` / etc. packages.
- Replace remaining hardcoded project-specific values (`appcloud.systems` defaults, container image paths, etc.) with CloudFormation template parameters: `ProjectName`, `DomainName`, `WebContainerImage`, and similar. (Resource/IAM scoping already derives from `DomainName`/`${AWS::StackName}` rather than the retired `taskmanager-*` literal; introducing `${ProjectName}` would decouple naming from the domain where that's desirable.) The branch model (shared-infrastructure branches: `app`/`beta`/`alpha`/`dev`; feature branches everywhere else) stays hardcoded *for this change* (configurable is a future extension).
- Extract the deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) into a **reusable workflow** with `on: workflow_call:` inputs. Inputs cover: project name, domain name, hosted-zone ID, multi-region branch list, secondary AWS region, container image source path, ECR repo naming pattern, and others identified during implementation. The workflow lives in this repo's `.github/workflows/deploy.yml` and is referenced by consumers as `uses: YadaYadaSoftware/awswebapp/.github/workflows/deploy.yml@v1`.
- The TaskManager repo (this one) becomes the **reference consumer** of its own library. Its `.github/workflows/zbuild.yml` is rewritten as a thin wrapper that (a) consumes the `YadaYada.AwsWebApp.DeploymentStack` NuGet package to download the templates into a local `infrastructure/` directory at workflow time, and (b) calls the reusable deploy workflow with its specific inputs. The existing `infrastructure/` directory in this repo becomes the *source of truth* for what gets packaged — it stays edited here, and CI publishes a new NuGet version on each merge.
- Define an **onboarding contract**: a `CONSUMING.md` document at the root that explains, for a new .NET web project starting from zero, exactly what they need to do — what NuGet package to install, what reusable workflow to call, what secrets to set, what AWS bootstrap to deploy manually first, and what conventions to follow (branch names, container shape, etc.).
- Out of scope but worth flagging in the spec: this change ships `.NET-only` build steps in the reusable workflow. A "bring-your-own-container" variant that skips the .NET build is feasible but explicitly deferred.

## Capabilities

### New Capabilities

- `reusable-deployment-stack`: How the CloudFormation templates and GitHub Actions deploy workflow are packaged for reuse by other .NET web-app projects in this organization. Covers: the NuGet package contract, the reusable workflow's input/secret surface, the parameter-pattern conventions templates must follow to be reusable, the onboarding flow for a new consumer project, and the versioning/upgrade strategy.

### Modified Capabilities

_None._ This change doesn't redefine any existing capability's requirements — it adds packaging on top of what's already there. `aurora-kms-key-management`, `multi-region-deployment-topology`, and `branch-stack-cleanup` continue to describe the deployed system's behavior; this change describes how the *recipe* for that deployed system is shared.

## Impact

**New artifacts:**
- **New NuGet package** `YadaYada.AwsWebApp.DeploymentStack` (working name) published to this repo's GitHub Packages NuGet feed. Contents: every file currently in `infrastructure/`, organized so a consumer can extract them at workflow time via `dotnet add package` + a small extraction step.
- **New reusable workflow** `.github/workflows/deploy.yml` with `on: workflow_call`. The bulk of the logic currently in `zbuild.yml`'s `deploy` job moves here.
- **New onboarding doc** `CONSUMING.md` at repo root.
- **New tag/release flow** — each merge to `app` publishes a new NuGet version AND a tagged workflow ref (`v<major>.<minor>.<patch>`).

**Modified in this repo (TaskManager consumer):**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — becomes a thin caller that installs the NuGet package, extracts templates into a local working dir, and invokes `deploy.yml` with TaskManager-specific inputs. The .NET build + test steps stay (they're TaskManager-specific) and run *before* the reusable workflow call.
- Every template in [infrastructure/](../../../infrastructure/) — remaining hardcoded `appcloud.systems` defaults / source-tree specifics replaced with `!Sub` references to new template parameters (`taskmanager` literals are already gone).
- [CLAUDE.md](../../../CLAUDE.md) — update the "Stack reality" and "Architecture" sections to reflect the package-based template distribution.

**External:**
- Other org repos that want to use this pattern can now install the NuGet package and call the reusable workflow. Migration from a "forked from awswebapp" repo to a "consumer of awswebapp" repo is a separate (per-consumer) exercise that this change enables but doesn't itself perform.

**Out of scope (explicitly):**
- Non-.NET consumers. Generic-build support would require splitting build from deploy in a way this change deliberately doesn't tackle.
- Public open-sourcing. The NuGet package can stay restricted to the org's GitHub Packages feed indefinitely.
- Migrating existing org repos to use the package. Each consumer makes that decision on their own timeline.
- The "extract to a separate library repo" option discussed during proposal. Staying in this repo for now; revisit if multiple consumer repos cause coordination friction.
- A CDK / Terraform alternative. Pure CloudFormation; same as today.
- Configurable branch model. The `app`/`beta`/`alpha`/`dev` shared-infrastructure branch set is hardcoded in this change. A consumer who wants `main` instead of `app` has to live with it for now or wait for a follow-up.
