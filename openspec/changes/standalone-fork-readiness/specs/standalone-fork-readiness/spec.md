## ADDED Requirements

### Requirement: Deploy workflow validates required configuration before deploying

The deploy caller workflow (`sample-deploy.yml`) SHALL run a preflight validation step before any
build or deploy job that checks every **required** repository variable and secret, and SHALL fail the
run with a non-zero exit and a single consolidated, actionable message listing **all** missing items
(not just the first). Required variables: `DOMAIN_NAME`, `AWS_REGION_PRIMARY`, `AWS_REGION_SECONDARY`,
`HOSTED_ZONE_ID`. Required secrets: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `DATABASE_PASSWORD`,
`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `FRAMEWORK_FEED_TOKEN`. Each missing item's message SHALL
name the item and where to set it. Optional config (`AWS_ACCESS_KEY_ID_PROD`/`_SECRET_…_PROD`,
`GOOGLE_TEST_ACCESS_TOKEN`/`_REFRESH_TOKEN`) SHALL be reported as a warning, not a failure.

#### Scenario: A required variable is missing
- **WHEN** the deploy workflow runs with `DOMAIN_NAME` (or any required variable) unset
- **THEN** the preflight fails the run before building, and the log names `DOMAIN_NAME` and where to set it

#### Scenario: A required secret is missing
- **WHEN** the deploy workflow runs without `FRAMEWORK_FEED_TOKEN` (or any required secret) set
- **THEN** the preflight fails the run before building, listing every missing required secret in one message

#### Scenario: All required config present
- **WHEN** every required variable and secret is set
- **THEN** the preflight passes and the build/deploy jobs proceed (no-op for a fully-configured repo such as TaskManager)

#### Scenario: Optional config missing
- **WHEN** only optional config (prod credentials or Google test tokens) is unset
- **THEN** the preflight warns about the optional items but does not fail the run

### Requirement: Framework-feed token is configurable for cross-org package restore

The deploy caller and the reusable workflow's container build SHALL source the GitHub Packages token
used to restore the framework packages from a configurable `FRAMEWORK_FEED_TOKEN` secret that
**defaults to the workflow `GITHUB_TOKEN`** when unset. This SHALL let a cross-organization fork
(consuming `Tjb.Web.Framework*` from YadaYadaSoftware's feed with a `read:packages` PAT) restore the
packages, while a same-organization caller (TaskManager) keeps using `GITHUB_TOKEN` with no new config.

#### Scenario: Same-org caller uses the default token
- **WHEN** a caller in the framework's own org runs without `FRAMEWORK_FEED_TOKEN` set
- **THEN** the framework packages restore using the workflow `GITHUB_TOKEN`, unchanged from before

#### Scenario: Cross-org fork uses its PAT
- **WHEN** a fork in a different org sets `FRAMEWORK_FEED_TOKEN` to a `read:packages` PAT on the framework's org
- **THEN** the build/test and the Docker image restore the framework packages using that token

### Requirement: Hosted zone is supplied via repository variable

The deploy caller SHALL pass `hosted-zone-id` from `vars.HOSTED_ZONE_ID` rather than a hardcoded
literal, so a fork supplies its own Route 53 hosted zone through configuration without editing
workflow code.

#### Scenario: Hosted zone read from configuration
- **WHEN** the deploy workflow runs
- **THEN** it passes `hosted-zone-id` from the `HOSTED_ZONE_ID` repository variable to the reusable deploy workflow

### Requirement: A new-repo setup guide exists in the root README

The root `README.md` SHALL contain a step-by-step "Setting up a new repo" section that lets someone
fork the repo and reach a first deploy without reading source. It SHALL cover: fork; what to keep vs
delete; promoting `src/sample` → `src/` (including the paths to update in `sample-deploy.yml`, the
Dockerfile, the solution, and `nuget.config`); configuring the framework feed + a pinned framework
version + the `read:packages` PAT; the complete required/optional variables-and-secrets checklist;
the AWS prerequisites (linking `src/sample/DEPLOYING.md`); the first build/test/deploy; and
troubleshooting that maps each preflight failure to its fix. The variables-and-secrets checklist in
the README SHALL match the preflight's required/optional lists exactly.

#### Scenario: A new consumer can self-serve
- **WHEN** a reader follows the "Setting up a new repo" section end-to-end without prior context
- **THEN** they can fork, configure the repo's variables/secrets and AWS prerequisites, and trigger a deploy

#### Scenario: Docs match the guard
- **WHEN** the README's required/optional config checklist is compared to the preflight's lists
- **THEN** they name the same variables and secrets

### Requirement: The sample README documents the GitHub deploy pipeline

The `src/sample/README.md` SHALL include a section describing how to deploy the sample through the
GitHub Actions pipeline (`sample-deploy.yml`), so a reader who has only run the sample locally can
discover and set up CI deployment. The section SHALL: state that pushes touching `src/sample/**`
trigger `sample-deploy.yml`; note the `SAMPLE_DEPLOY_ENABLED` gate and the `validate-config`
preflight; list the required repository Variables and Secrets; and link to `src/sample/DEPLOYING.md`
(operator/AWS prerequisites) and the root README "Setting up a new repo" guide (fork path). The
variables-and-secrets checklist in the sample README SHALL match the preflight's lists.

#### Scenario: Reader finds pipeline setup from the sample README
- **WHEN** a reader who has only followed the local-run instructions opens `src/sample/README.md`
- **THEN** a section explains the sample deploys via `sample-deploy.yml`, names the enabling `SAMPLE_DEPLOY_ENABLED` variable and the preflight, lists the required Variables/Secrets, and links to `DEPLOYING.md` and the root README setup guide for the full AWS prerequisites

### Requirement: The setup docs give step-by-step bootstrap-stack deployment instructions

The setup documentation SHALL include concrete, copy-pasteable instructions for deploying the
per-region bootstrap stack (`infrastructure/bootstrap.template`) — in `src/sample/DEPLOYING.md`,
referenced from the root README new-repo guide — so a fork operator can stand up the account/region
prerequisites without reading the template. The instructions SHALL cover at minimum: the stack-name convention (=
the dashed deployment domain); the `aws cloudformation` deploy command with
`--capabilities CAPABILITY_NAMED_IAM`; the primary-vs-replica region model (the primary region leaves
`PrimaryNonprodKeyArn`/`PrimaryProdKeyArn` empty and owns the IAM users + multi-region KMS keys, while
the secondary region passes the primary stack's `AuroraKmsKeyNonprodArn`/`AuroraKmsKeyProdArn` outputs
into those parameters); the required deploy order (primary region first, then secondary); and the fact
that the primary stack's `GitHubActionsUser*` outputs are the source of the
`AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` (and `_PROD`) repository secrets named in the config
checklist.

#### Scenario: Operator deploys bootstrap from the docs
- **WHEN** a fork operator follows the bootstrap deployment instructions for a new domain
- **THEN** they can deploy the bootstrap stack in the primary region, read its KMS-key ARNs and CI access-key outputs, and deploy the secondary-region stack with the primary's KMS ARNs — without reading the template

#### Scenario: Bootstrap outputs map to the config checklist
- **WHEN** the operator needs the AWS credential secrets named in the config preflight
- **THEN** the docs identify the primary bootstrap stack's `GitHubActionsUser*` outputs as the source of `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` (and the optional `_PROD` secrets)

### Requirement: Workflows are classified for reuse by a fork

The setup documentation SHALL classify each workflow under `.github/workflows/` as KEEP (reusable
as-is), ADAPT (a template the fork customizes), or DELETE/replace (origin-repo-specific), so a fork
knows what to reuse. At minimum it SHALL classify `deploy.yml` (KEEP), `cleanup-on-branch-delete.yml`
(KEEP), `sample-deploy.yml` (ADAPT), and `zbuild.yml` (DELETE/replace).

#### Scenario: Fork reads the workflow inventory
- **WHEN** a fork owner consults the setup documentation
- **THEN** each workflow is listed with a KEEP/ADAPT/DELETE verdict and a one-line reason
