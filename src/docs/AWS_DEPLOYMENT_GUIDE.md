# AWS Deployment Guide

> **Authoritative source:** the deploy workflow
> [.github/workflows/zbuild.yml](../../.github/workflows/zbuild.yml) (job name
> "Deploy Everything") and the templates under
> [infrastructure/](../../infrastructure/). This guide describes what that
> workflow actually does; if it and this doc disagree, trust the workflow.

## What gets deployed

The deployed application is the **`Tjb.Web` Blazor Server container** running on
**ECS Fargate behind an ALB**, backed by **Aurora MySQL Serverless v2**. There
is no Lambda function, no API Gateway, and no RDS PostgreSQL involved in the live
path. Infrastructure is provisioned by **AWS SAM-packaged, nested
CloudFormation** templates.

## Trigger

Every push to any branch (paths-ignore excludes `.md`, `scripts/**`,
`.vscode/**`, and `openspec/**/*.yaml`) runs the `Deploy Everything` workflow.
Add `nodeploy` to a non-merge commit message to run build/test but skip the
deploy + UI-test + publish jobs. A new push to any **non-`app`** branch cancels
the in-flight run for that branch; `app` pushes queue instead.

## Pipeline stages (jobs)

1. **Get Branch Name** — computes the branch leaf (`{type}/{name}` → `{name}`)
   and the environment (`app`→Production, `test`→Staging, else
   Development), and detects `nodeploy`.

2. **Build and Test Applications**
   - Sets up .NET `10.0.x`, computes a version via GitVersion.
   - `dotnet restore` / `dotnet build -c Release`.
   - Runs unit tests excluding UI tests:
     `dotnet test --filter "FullyQualifiedName!~Tjb.UiTests"`, reported via
     `dorny/test-reporter` (TRX `unit-tests.trx`).
   - `dotnet pack`s `Tjb.Shared`, `Tjb.Data`, `Tjb.Api`, `Tjb.Migrations`,
     `Tjb.Web` (branch-suffixed versions for non-`app` branches).
   - `dotnet publish`es `Tjb.Web` for the container.

3. **Deploy to AWS** (matrix over regions)
   - Runs in `AWS_REGION_PRIMARY` always; also `AWS_REGION_SECONDARY` for
     `app`/`test` (multi-region).
   - Selects credentials by branch: `app` uses the `*_PROD` secrets; all other
     branches use the non-prod secrets.
   - Processes `secrets.DOMAIN_NAME` into its dashed form (`.`→`-`).
   - **Builds & packages the template with SAM**: `sam build` then `sam package`
     to the templates S3 bucket
     (`{account}-{dashed-domain}-{region}`), uploading the packaged template
     under the branch prefix. Branch selects the template:
     - `app`/`test`/`dev` → `infrastructure/master.template`
     - all other branches → `infrastructure/application.template`
   - **Builds & pushes the Docker image** to ECR
     (`{account}.dkr.ecr.{region}.amazonaws.com/{dashed-domain}`). The image is
     content-addressed by a SHA256 of `src/`; if that tag already exists in ECR
     the build is skipped. Also tags with the SemVer and branch name.
   - Looks up bootstrap-owned values from SSM (Aurora KMS key ARN and the Aurora
     cluster delete-handler Lambda ARN) under
     `/{dashed-domain}/...` paths.
   - For secondary regions, reads primary-region stack outputs (global cluster
     ID, primary LB DNS / hosted-zone) to wire up failover.
   - **Deploys the CloudFormation stack** via
     `aws-actions/aws-cloudformation-github-deploy` with stack name
     `{branch-leaf}-{dashed-domain}` and the packaged template URL, passing
     parameter overrides (branch, domain, container image, Google OAuth, DB
     password for master-template branches, capacities, multi-region flags,
     KMS/teardown ARNs, etc.). Capabilities:
     `CAPABILITY_NAMED_IAM,CAPABILITY_AUTO_EXPAND`.

4. **Post-Deployment UI Tests** — runs `Tjb.UiTests` (Playwright/xUnit) against
   the deployed URL `https://{branch-leaf}.{DOMAIN_NAME}` using token-based
   Google auth. Results reported via `dorny/test-reporter` (`ui-tests.trx`) plus
   uploaded artifacts (TRX, screenshots, Playwright traces).

5. **Publish to GitHub Packages** — pushes the packed NuGets to the repo's
   GitHub Packages feed.

## Where the app lands

- **Stack name**: `{branch-leaf}-{dashed-domain}` (e.g. `dev-appcloud-systems`,
  `app-appcloud-systems`).
- **URL**: `https://{branch-leaf}.{DOMAIN_NAME}` (e.g.
  `https://app.appcloud.systems`).
- **Production branch**: `app` (also GitVersion `main`).

## Manual / local notes

- Validate a template locally with SAM before pushing:
  `sam build --template infrastructure/master.template`.
- Inspect a deployed stack:
  `aws cloudformation describe-stacks --stack-name {branch-leaf}-{dashed-domain} --region <region>`.
- ECS service logs go to the CloudWatch log group created in
  [infrastructure/web.template](../../infrastructure/web.template); tail with
  `aws logs tail <log-group> --follow`.

## Prerequisites

See [AWS_DEPLOYMENT_SETUP.md](AWS_DEPLOYMENT_SETUP.md) for the GitHub
secrets/variables and the per-region bootstrap stack that the pipeline depends
on. For the model behind it (nested stacks, multi-region), see
[DEPLOYMENT_STRATEGY.md](DEPLOYMENT_STRATEGY.md).

## Reading a red run

Start at the run-page **summary** (the `Unit tests` / `UI tests` reports and the
`ui-test-artifacts-*` artifact containing Playwright `trace.zip`), not the raw
step logs. TRX filenames are pinned (`unit-tests.trx`, `ui-tests.trx`).
