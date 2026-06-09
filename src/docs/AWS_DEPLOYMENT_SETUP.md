# AWS Deployment Setup

> **Authoritative sources:** [.github/workflows/zbuild.yml](../../.github/workflows/zbuild.yml),
> [infrastructure/bootstrap.template](../../infrastructure/bootstrap.template),
> and the "Infrastructure naming convention" / "Branch model & CI/CD" sections
> of [CLAUDE.md](../../CLAUDE.md). This guide lists the prerequisites the deploy
> pipeline expects; trust those files where they differ.

## What the pipeline deploys

A push triggers the `Deploy Everything` workflow, which builds and tests the
solution, builds the `Tjb.Web` Docker image and pushes it to ECR, packages the
nested CloudFormation templates with AWS SAM, deploys the env CloudFormation
stack, and runs UI tests. The result is a Blazor Server container on **ECS
Fargate behind an ALB**, backed by **Aurora MySQL Serverless v2** — not Lambda,
API Gateway, or RDS PostgreSQL. The workflow file is **`zbuild.yml`**.

## Prerequisites

### 1. AWS accounts & the bootstrap stack

Deploy the **bootstrap stack** once per region you intend to deploy into
([infrastructure/bootstrap.template](../../infrastructure/bootstrap.template)).
Its stack name **is the dashed domain** (e.g. `appcloud-systems`) — no
`bootstrap-` prefix. It owns the per-domain shared resources the pipeline looks
up, including:

- ECR repository (named for the dashed domain).
- Templates S3 bucket: `{account}-{dashed-domain}-{region}`.
- KMS keys for Aurora (prod + nonprod), published to SSM at
  `/{dashed-domain}/kms/{prod,nonprod}/aurora-key-arn`.
- The Aurora cluster delete-handler Lambda ARN, published to SSM at
  `/{dashed-domain}/lambda/aurora-cluster-delete-handler-arn`.
- GitHub Actions IAM users (`{dashed-domain}-GitHubActionsUser` and
  `...UserProd`) whose access keys become the GitHub secrets below.

The deploy job fails fast if these SSM parameters are missing in the target
region, so deploy bootstrap first.

### 2. GitHub repository **secrets**

Settings → Secrets and variables → Actions → **Secrets**:

| Secret | Purpose |
|---|---|
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Non-prod credentials (every branch except `app`). |
| `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` | Prod credentials (used only by the `app` branch; the deploy fails if missing on an `app` deploy). |
| `DOMAIN_NAME` | Deployment domain in **dot** form (e.g. `appcloud.systems`). The pipeline derives the dashed form. |
| `DATABASE_PASSWORD` | Aurora master password (min 8 chars). Used for master-template branches. |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Google OAuth credentials. |
| `GOOGLE_TEST_ACCESS_TOKEN` / `GOOGLE_TEST_REFRESH_TOKEN` | Token-based Google auth for post-deploy UI tests. |

(`GITHUB_TOKEN` is provided automatically and is used to publish NuGets.)

### 3. GitHub repository **variables**

Settings → Secrets and variables → Actions → **Variables**:

| Variable | Purpose |
|---|---|
| `AWS_REGION_PRIMARY` | Primary region (currently `us-east-1`). |
| `AWS_REGION_SECONDARY` | Secondary region for multi-region branches (currently `us-east-2`). |
| `DRIFT_GUARD_FAIL_ON_DEV` | Optional. If `true`, fails `dev` runs when a default-branch-only workflow has drifted from `app`. |

The workflow has **no fallback** for the region variables — leaving them unset
fails the deploy matrix.

### 4. Route 53

A hosted zone for the domain must exist (the workflow passes a hardcoded
`HostedZoneId` parameter). ACM certificates for `{branch}.{DomainName}` are
created and DNS-validated by the templates.

## Branch model (summary)

- **`app`** is the production branch (and GitVersion `main`). Solo developer; no
  pull requests — changes are merged/pushed directly.
- **`app` / `test`** deploy `master.template` **multi-region**
  (`AWS_REGION_PRIMARY` + `AWS_REGION_SECONDARY`), including an Aurora Global
  Cluster.
- **`dev`** deploys `master.template` single-region.
- **Every other branch** (`{type}/{name}`, type ∈
  `build|deploy|system|feature|fix`, plus bare-named OpenSpec change branches)
  deploys `application.template` single-region, importing backend exports from
  `dev`.

See [CLAUDE.md](../../CLAUDE.md) for the full branch/CI rules.

## Resource naming convention

Names are **derived, not hardcoded**, from `DOMAIN_NAME` and the branch:

- **Env CloudFormation stack**: `{branch-leaf}-{dashed-domain}`
  (e.g. `dev-appcloud-systems`, `app-appcloud-systems`).
- **App URL**: `https://{branch-leaf}.{DOMAIN_NAME}`.
- **Templates bucket**: `{account}-{dashed-domain}-{region}`.
- **ECR repo / bootstrap stack**: the dashed domain (e.g. `appcloud-systems`).
- **SSM lookup paths**: `/{dashed-domain}/...`.

Do not reintroduce literal project names or region values anywhere — the
pipeline is domain- and region-agnostic by design.

## See also

- [AWS_DEPLOYMENT_GUIDE.md](AWS_DEPLOYMENT_GUIDE.md) — what each pipeline stage does.
- [DEPLOYMENT_STRATEGY.md](DEPLOYMENT_STRATEGY.md) — the nested-stack / multi-region model.
- [ARCHITECTURE.md](ARCHITECTURE.md) — the system and its projects.
