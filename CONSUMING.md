# Consuming the AWS deploy stack

This repo is **also a reusable deployment library**. Another .NET web app can deploy onto the
same AWS machinery this project uses — Aurora Serverless v2 (optionally a multi-region Global
Cluster) behind ECS Fargate + an ALB, with Route 53 DNS, per-branch CloudFormation stacks, and
branch-conditional prod/nonprod CI credentials — by:

1. Pre-deploying one **bootstrap** stack per AWS region (manual, one-time).
2. Installing the **`YadaYada.AwsWebApp.DeploymentStack`** NuGet package (the CloudFormation
   templates).
3. Calling the **reusable deploy workflow** (`.github/workflows/deploy.yml`) from your own CI
   with ~15 inputs and a set of secrets.

Naming is **derived from your domain** — there is no separate project-name knob. The dashed form
of `domain-name` (e.g. `example.com` → `example-com`) is what every resource, SSM path, secret
name, IAM scope, and stack name is built from. See "Infrastructure naming convention" in
[CLAUDE.md](CLAUDE.md).

> This document describes the contract as of Phase 3 of the `make-deployment-stack-reusable`
> change. The reusable-workflow path has been verified for this repo's own (self-consume)
> deploys; the external-consumer template-extraction path (`deployment-stack-version`) is
> implemented but not yet exercised by a real second consumer.

---

## 1. Prerequisites

- **AWS account.** The account model is **namespaced/shared**: multiple consumers can share one
  account because every resource name carries the dashed domain. The true account-singletons
  (`AWS::ApiGateway::Account`, the `cf-templates-${AccountId}-${Region}` bucket policy) can only
  be owned once per account — if you share an account with another consumer, exactly one of you
  owns those, or they move to a higher-level account-bootstrap stack.
- **A registered domain + a Route 53 hosted zone** for it (you supply the hosted-zone ID).
- **A .NET web app** that builds a Docker image (a `Dockerfile` whose build context is the repo
  root) and, optionally, a Playwright UI-test project.
- **GitHub repo** with Actions enabled and the secrets/variables below configured.

## 2. One-time bootstrap (manual, per region)

Deploy [`infrastructure/bootstrap.template`](infrastructure/bootstrap.template) once per region
you deploy to. The **stack name must be your dashed domain** (e.g. `example-com`). It owns the
KMS keys, the GitHub Actions IAM users, the ECR repository, the templates S3 bucket, and — via
the Aurora teardown Lambda — the SSM parameters the deploy workflow reads:

- `/<dashed-domain>/kms/{prod,nonprod}/aurora-key-arn`
- `/<dashed-domain>/lambda/aurora-cluster-delete-handler-arn`

Take the `GitHubActionsUser` / `GitHubActionsUserProd` access keys it outputs and store them as
the AWS secrets below.

## 3. Install the templates package

Add a `PackageReference` to `YadaYada.AwsWebApp.DeploymentStack` (published to this org's GitHub
Packages feed) in any project, **or** rely on the workflow's built-in extraction (set
`deployment-stack-version`, below). The package ships every template under
`contentFiles/any/any/infrastructure/`.

## 4. Call the reusable workflow

Your caller workflow runs your own build/test, then calls `deploy.yml`. Because reusable
workflows run as separate jobs, the deploy job re-checks-out **your** repo — so either commit the
templates to your `infrastructure/` directory, or set `deployment-stack-version` to have the
workflow extract them from the NuGet package at run time.

```yaml
# .github/workflows/deploy-myapp.yml (in YOUR repo)
name: Deploy MyApp
on:
  push:
    branches: ['**']
jobs:
  meta:
    runs-on: ubuntu-latest
    outputs:
      branch-name: ${{ steps.b.outputs.branch-name }}
      environment: ${{ steps.b.outputs.environment }}
      version: ${{ steps.v.outputs.version }}
    steps:
      - id: b
        run: |
          LEAF=${GITHUB_REF#refs/heads/}; LEAF=${LEAF##*/}
          case "$LEAF" in
            app) ENV=Production ;;
            test) ENV=Staging ;;
            *) ENV=Development ;;
          esac
          echo "branch-name=$LEAF" >> $GITHUB_OUTPUT
          echo "environment=$ENV" >> $GITHUB_OUTPUT
      - id: v
        run: echo "version=1.0.${{ github.run_number }}" >> $GITHUB_OUTPUT

  deploy:
    needs: [meta]
    uses: YadaYadaSoftware/awswebapp/.github/workflows/deploy.yml@v1
    with:
      branch-name: ${{ needs.meta.outputs.branch-name }}
      environment: ${{ needs.meta.outputs.environment }}
      custom-version: ${{ needs.meta.outputs.version }}
      domain-name: ${{ vars.DOMAIN_NAME }}        # e.g. example.com
      hosted-zone-id: ${{ vars.HOSTED_ZONE_ID }}
      region-primary: ${{ vars.AWS_REGION_PRIMARY }}
      region-secondary: ${{ vars.AWS_REGION_SECONDARY }}
      web-dockerfile-path: src/MyApp.Web/Dockerfile
      web-image-name: myapp-web
      ui-tests-project-path: src/MyApp.UiTests
      deployment-stack-version: "1.4.0"           # omit if you vendor infrastructure/
    secrets:
      AWS_ACCESS_KEY_ID: ${{ secrets.AWS_ACCESS_KEY_ID }}
      AWS_SECRET_ACCESS_KEY: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
      AWS_ACCESS_KEY_ID_PROD: ${{ secrets.AWS_ACCESS_KEY_ID_PROD }}
      AWS_SECRET_ACCESS_KEY_PROD: ${{ secrets.AWS_SECRET_ACCESS_KEY_PROD }}
      DATABASE_PASSWORD: ${{ secrets.DATABASE_PASSWORD }}
      GOOGLE_CLIENT_ID: ${{ secrets.GOOGLE_CLIENT_ID }}
      GOOGLE_CLIENT_SECRET: ${{ secrets.GOOGLE_CLIENT_SECRET }}
      GOOGLE_TEST_ACCESS_TOKEN: ${{ secrets.GOOGLE_TEST_ACCESS_TOKEN }}
      GOOGLE_TEST_REFRESH_TOKEN: ${{ secrets.GOOGLE_TEST_REFRESH_TOKEN }}
      DEPLOYMENT_STACK_FEED_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

> This repo dogfoods the same workflow from [`zbuild.yml`](.github/workflows/zbuild.yml) with
> `uses: ./.github/workflows/deploy.yml` and `secrets: inherit` (allowed only for same-repo
> calls). External callers must pass secrets explicitly as shown.

## 5. Input reference

| Input | Required | Default | Purpose |
| --- | --- | --- | --- |
| `branch-name` | yes | – | Branch leaf used as the stack-name suffix and deployed subdomain. |
| `environment` | yes | – | Environment label (`Production`/`Staging`/`Development`). |
| `custom-version` | yes | – | SemVer tag applied to the container image. |
| `assembly-sem-ver` | no | `""` | Value of the Docker `ASSEMBLY_VERSION` build-arg. |
| `domain-name` | yes | – | Domain in dot form (e.g. `example.com`). The **sole naming input**. |
| `hosted-zone-id` | yes | – | Route 53 hosted zone ID for the domain. |
| `region-primary` | yes | – | Primary AWS region. |
| `region-secondary` | yes | – | Secondary AWS region (used by multi-region branches). |
| `multi-region-branches` | no | `app test` | Space-separated leaves that deploy to both regions. |
| `shared-infra-branches` | no | `app test dev` | Space-separated leaves that deploy the full backend (master template). |
| `prod-branch` | no | `app` | Leaf that receives prod credentials + the prod KMS key. |
| `engine-version` | no | `8.0.mysql_aurora.3.10.0` | Aurora MySQL engine version. |
| `dotnet-version` | no | `10.0.x` | .NET SDK version for the UI-test job (and template extraction). |
| `web-dockerfile-path` | no | `src/Tjb.Web/Dockerfile` | Web app Dockerfile (build context = repo root). |
| `web-image-name` | no | `tjb-web` | Local docker tag base for the web image. |
| `ui-tests-project-path` | no | `src/Tjb.UiTests` | Playwright UI-test project. |
| `deployment-stack-version` | no | `""` | NuGet version of the templates package to extract. Leave empty if your checkout already contains `infrastructure/`. |
| `deployment-stack-feed` | no | `https://nuget.pkg.github.com/YadaYadaSoftware/index.json` | Feed hosting the templates package (used only when `deployment-stack-version` is set). |

## 6. Secret reference

| Secret | Required | Purpose |
| --- | --- | --- |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | yes | Non-prod CI credentials (bootstrap `GitHubActionsUser`). |
| `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` | only for the prod branch | Prod CI credentials (`GitHubActionsUserProd`). The workflow fails loudly on a prod-branch deploy if these are missing. |
| `DATABASE_PASSWORD` | yes | Aurora cluster admin password. |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | yes | Google OAuth credentials. |
| `GOOGLE_TEST_ACCESS_TOKEN` / `GOOGLE_TEST_REFRESH_TOKEN` | no | Token-based auth for the post-deploy UI tests. |
| `DEPLOYMENT_STACK_FEED_TOKEN` | only when extracting | Token to read `deployment-stack-feed` (e.g. `GITHUB_TOKEN`). |

## 7. Branch model

Baked in (not yet configurable beyond the branch-list inputs):

- **`prod-branch`** (default `app`) → Production, prod credentials, prod KMS key, larger Aurora
  capacity, multi-region.
- **`shared-infra-branches`** (default `app test dev`) → deploy the full backend via
  `master.template` (Aurora cluster, networking, security).
- **`multi-region-branches`** (default `app test`) → deploy to both regions with an Aurora
  Global Cluster.
- **Every other branch** → a feature stack via `application.template`, single region, nonprod
  credentials, importing the backend exports from `dev`. Deployed at
  `https://<branch-leaf>.<domain>`.

## 8. Troubleshooting

- **`SSM parameter … is empty or missing`** — the bootstrap stack isn't deployed in that region,
  or its name isn't your dashed domain. Deploy/rename it (§2).
- **Empty domain in bucket/stack names** — `domain-name` resolved empty. If you pass
  `${{ vars.DOMAIN_NAME }}`, confirm it's a repo **Variable** (not a Secret) — `vars.*` and
  `secrets.*` are different namespaces.
- **`templates not found … after restore`** — `deployment-stack-version` points at a version not
  on `deployment-stack-feed`, or `DEPLOYMENT_STACK_FEED_TOKEN` can't read the feed.
- **Prod deploy fails immediately** — `AWS_ACCESS_KEY_ID_PROD`/`_SECRET_…_PROD` not set; required
  only for the `prod-branch`.
- **Secrets in `with:`** — you can't reference `${{ secrets.* }}` from a reusable-workflow `with:`
  block. Pass non-sensitive values (like the domain) as repo **Variables**; pass real secrets via
  the `secrets:` block.
