# Deploying the sample (operator runbook)

The sample deploys through the **reusable `deploy.yml`** workflow (from
`make-deployment-stack-reusable`), driven by
[`.github/workflows/sample-deploy.yml`](../../.github/workflows/sample-deploy.yml). That workflow
is **gated off** until you complete the AWS prerequisites below and flip a switch — so it can sit
in the repo without affecting TaskManager's CI.

## Naming model (why a subdomain)

The reusable stack **derives all resource naming from the domain** (dashed form) — there is **no
`ProjectName`**. So the sample gets its own namespace by using a **subdomain as its `domain-name`**:

- `domain-name = sample.appcloud.systems` → dashed **`sample-appcloud-systems`**.
- Its bootstrap stack, KMS aliases, SSM paths, ECR repo, Aurora identifiers, and CloudFormation
  stack names (`{leaf}-sample-appcloud-systems`) are therefore **distinct from TaskManager's**
  `appcloud-systems`. URLs are `{leaf}.sample.appcloud.systems`.
- DNS records live in the **existing `appcloud.systems` hosted zone** (`Z06422172SASV44F5Y8VA`) — no
  new domain registration.

> Do **not** point the sample at `appcloud.systems` directly: a feature-branch sample imports
> `dev`'s backend Aurora, so its Identity/`AspNetUsers` migration would collide in TaskManager's
> dev database. The distinct dashed domain avoids that.

## Prerequisites (per region you deploy to)

1. **Bootstrap stack (one per region)** — deploy [`infrastructure/bootstrap.template`](../../infrastructure/bootstrap.template)
   with **stack name = your dashed domain** (`sample-appcloud-systems` for the sample) in **each**
   region. It provisions the KMS keys, GitHub Actions IAM users, ECR repo, templates bucket, and the
   SSM parameters the deploy reads (`/sample-appcloud-systems/kms/*`,
   `/sample-appcloud-systems/lambda/aurora-cluster-delete-handler-arn`).

   The template is ~47 KB — under CloudFormation's 51,200-byte inline limit — so a first deploy uses
   `--template-file` directly with **no S3 upload** (the templates bucket doesn't exist yet on the
   first deploy; this stack creates it). If the template ever grows past ~51 KB, add `--s3-bucket
   <a bucket>` so the CLI uploads it first.

   **Primary vs. replica is decided by parameters, not a flag.** The **primary** region (here
   `us-east-1`) leaves `PrimaryNonprodKeyArn`/`PrimaryProdKeyArn` **empty** and owns the account-wide
   IAM users and the multi-region KMS keys. The **secondary** region (`us-east-2`) is a *replica*:
   pass the primary stack's two KMS-key ARN outputs into those parameters. **So deploy the primary
   region first**, then the secondary:

   ```bash
   DOMAIN_DASHED=sample-appcloud-systems     # your domain with dots replaced by dashes

   # 1) PRIMARY (us-east-1): empty KMS params => owns IAM users + multi-region KMS keys
   aws cloudformation deploy \
     --region us-east-1 \
     --stack-name "$DOMAIN_DASHED" \
     --template-file infrastructure/bootstrap.template \
     --capabilities CAPABILITY_NAMED_IAM

   # 2) read the primary KMS-key ARNs to feed the replica region
   NONPROD=$(aws cloudformation describe-stacks --region us-east-1 --stack-name "$DOMAIN_DASHED" \
     --query "Stacks[0].Outputs[?OutputKey=='AuroraKmsKeyNonprodArn'].OutputValue" --output text)
   PROD=$(aws cloudformation describe-stacks --region us-east-1 --stack-name "$DOMAIN_DASHED" \
     --query "Stacks[0].Outputs[?OutputKey=='AuroraKmsKeyProdArn'].OutputValue" --output text)

   # 3) SECONDARY (us-east-2): pass primary KMS ARNs => replica keys, no IAM users (they're global)
   aws cloudformation deploy \
     --region us-east-2 \
     --stack-name "$DOMAIN_DASHED" \
     --template-file infrastructure/bootstrap.template \
     --capabilities CAPABILITY_NAMED_IAM \
     --parameter-overrides PrimaryNonprodKeyArn="$NONPROD" PrimaryProdKeyArn="$PROD"
   ```

   > `RetentionDays` defaults to 7 (CloudWatch log retention); add it to `--parameter-overrides` to
   > change it. `CAPABILITY_NAMED_IAM` is required because the stack creates **named** IAM users/roles.

   **The primary stack's outputs are your CI credentials.** `GitHubActionsUserAccessKeyId` /
   `GitHubActionsUserSecretAccessKey` are the values for the **`AWS_ACCESS_KEY_ID`** /
   **`AWS_SECRET_ACCESS_KEY`** repo Secrets; `GitHubActionsUserProdAccessKeyId` /
   `GitHubActionsUserProdSecretAccessKey` are the optional **`AWS_ACCESS_KEY_ID_PROD`** /
   **`AWS_SECRET_ACCESS_KEY_PROD`** secrets. Read them once (they're only retrievable from outputs
   right after create — rotate via IAM afterward):

   ```bash
   aws cloudformation describe-stacks --region us-east-1 --stack-name "$DOMAIN_DASHED" \
     --query "Stacks[0].Outputs[?starts_with(OutputKey,'GitHubActionsUser')]" --output table
   ```

   - **Account-singleton caveat:** in a shared AWS account, `AWS::ApiGateway::Account` and the
     `cf-templates-${AccountId}-${Region}` bucket policy can only be owned once. If TaskManager's
     `appcloud-systems` bootstrap already owns them, the sample bootstrap must not re-declare them
     (or hoist them to an account-level stack) — see make-deployment-stack-reusable task 1.3.
   - **Git Bash path gotcha (Windows):** AWS CLI args that start with `/` (e.g. SSM `--name`) get
     mangled by MSYS path conversion → false `ParameterNotFound`. Run these from **PowerShell**, or
     prefix the command with `MSYS_NO_PATHCONV=1`.
2. **DNS / ACM** — the deploy creates `{leaf}.sample.appcloud.systems` records + ACM validation in
   the existing hosted zone `Z06422172SASV44F5Y8VA` (already passed as `hosted-zone-id`). Ensure
   that zone is in this account.
3. **A sample `dev` backend** — feature branches import `dev`'s backend exports, so deploy the
   sample's `dev` first (`dev` leaf → `master.template` → its **own Aurora cluster** under
   `dev-sample-appcloud-systems`). **This is a second Aurora cluster — real cost.** Do this before
   pushing sample feature branches.
4. **SES (optional)** — the framework sends a confirmation email on first sign-in
   (`RequireConfirmedAccount = true`). Either run the per-region SES identity setup for
   `sample.appcloud.systems` (DKIM + sandbox/production) or accept that the confirmation email
   won't send. See the SES section in the root [CLAUDE.md](../../CLAUDE.md).

## Enabling the deploy

Once the prerequisites exist, set the repo **Variable** `SAMPLE_DEPLOY_ENABLED = true`, plus
`HOSTED_ZONE_ID` (`sample-deploy.yml` reads the zone from `vars.HOSTED_ZONE_ID`). A
`validate-config` preflight runs first and fails with a checklist if any required Variable/Secret is
missing.

> **Cross-org forks:** the framework packages live on this org's GitHub Packages feed, so a fork in a
> different org can't restore them with the workflow `GITHUB_TOKEN` — create a `read:packages` PAT and
> set it as the secret **`FRAMEWORK_FEED_TOKEN`**. Same-org TaskManager needs nothing (it falls back
> to `GITHUB_TOKEN`).

Then a push touching `src/sample/**` runs `sample-deploy.yml`: it builds/tests `Sample.sln`
(restoring the framework with `FRAMEWORK_FEED_TOKEN` or the workflow `GITHUB_TOKEN`), then calls `deploy.yml` with
`domain-name=sample.appcloud.systems`, `web-dockerfile-path=src/sample/Sample.Web/Dockerfile`,
`web-image-name=sample-web`, `ui-tests-project-path=src/sample/Sample.UiTests`. The reusable
workflow deploys (single-region on feature branches, multi-region on `app`/`test`) and runs
the sample UI tests against `https://{leaf}.sample.appcloud.systems`.

**Cost gate:** start with a **feature branch** (single region). Only deploy to the shared-infra
branches (`dev`/`test`/`app` — `test`/`app` multi-region, second Global Cluster) when you accept the
cost (sample-ci-deploy §7, gated on explicit approval).

## Reconciled input mapping (vs the sample's original design)

`deploy.yml` has **no `project-name`** (derive-from-domain), no `web-project-path`, and no
`ui-test-filter`. The sample uses: `domain-name`, `hosted-zone-id`, `region-primary`/`-secondary`,
`web-dockerfile-path`, `web-image-name`, `ui-tests-project-path`. `deployment-stack-version` stays
empty because the caller lives in this repo (the checkout already contains `infrastructure/`).
