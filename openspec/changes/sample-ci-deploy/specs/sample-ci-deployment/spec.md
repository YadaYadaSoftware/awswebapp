## ADDED Requirements

### Requirement: The sample deploys via the reusable deployment stack

The sample's CI SHALL deploy `src/sample` by invoking the reusable deploy workflow delivered by `make-deployment-stack-reusable`, passing the sample's inputs (project name, domain, hosted zone, `web-project-path`, Dockerfile path, regions, multi-region branch list) and secrets, rather than carrying its own copy of the CloudFormation templates or deploy logic.

#### Scenario: Sample CI calls the reusable workflow

- **WHEN** the sample's CI workflow runs on a branch
- **THEN** it SHALL `uses:` the reusable deploy workflow and supply the sample's `project-name`, `domain-name`, `hosted-zone-id`, `web-project-path` (`src/sample/Sample.Web`), Dockerfile path, and required secrets

#### Scenario: Templates come from the deployment-stack package

- **WHEN** the reusable workflow runs for the sample
- **THEN** the CloudFormation templates SHALL be extracted from the `YadaYada.AwsWebApp.DeploymentStack` package, not from sample-owned template files

### Requirement: The sample builds and tests in CI on the published framework

The sample's CI SHALL build and test `Sample.sln` restoring `Tjb.Web.Framework`, `Tjb.Web.Hosting`, and `Tjb.Shared` from GitHub Packages, before the deploy job runs.

#### Scenario: CI restores the framework and builds the sample

- **WHEN** the sample's CI build job runs
- **THEN** it SHALL restore the framework packages from GitHub Packages, build `Sample.sln`, and run the sample's unit tests, failing the run if any step fails

### Requirement: The sample deploys to its own stacks across the branch model

The sample SHALL deploy to its own CloudFormation stacks named per the `{branch-leaf}-{processed-domain}` convention — multi-region on the shared-infrastructure branches (`app`/`beta`/`alpha`) and single-region on `dev` and feature branches — without colliding with TaskManager's stacks.

#### Scenario: Shared-infrastructure branch deploys multi-region

- **WHEN** the sample is deployed on `app` (or `beta`/`alpha`)
- **THEN** it SHALL create stacks in both the primary and secondary regions and resolve to `https://<sample-leaf>.{DOMAIN_NAME}`

#### Scenario: Feature branch deploys single-region

- **WHEN** the sample is deployed on a feature branch
- **THEN** it SHALL create a single-region stack consuming the backend exports as the established convention dictates, with no naming collision against TaskManager's stacks

### Requirement: The sample's deployed app passes UI tests

The sample SHALL have UI tests (Playwright/xUnit) that run against the deployed sample URL in CI, reported via the same TRX → test-reporter summary + artifact mechanism as `Tjb.UiTests`.

#### Scenario: UI tests run against the deployed sample

- **WHEN** the sample deploy completes on a branch
- **THEN** `Sample.UiTests` SHALL run against `https://<sample-leaf>.{DOMAIN_NAME}`, exercise login and the Guestbook page, and publish a TRX-based summary with uploaded artifacts

### Requirement: The sample inherits tagging and cleanup

The sample's stacks SHALL carry the six stack-level resource tags and SHALL be torn down by the branch-delete cleanup workflow, inheriting both behaviors from the reusable deployment stack with no sample-specific implementation.

#### Scenario: Sample stacks are tagged

- **WHEN** the sample is deployed
- **THEN** its stacks and propagated resources SHALL carry the canonical stack-level tag set (`Stack Name`, `Create Date`, `Branch`, `Specification`, `Version`, `DeployRunUrl`)

#### Scenario: Deleting a sample feature branch tears down its stack

- **WHEN** a non-protected sample feature branch is deleted
- **THEN** the cleanup workflow SHALL delete the corresponding `{branch-leaf}-{processed-domain}` stack and clear its templates-bucket prefix, leaving TaskManager's and the bootstrap's resources untouched
