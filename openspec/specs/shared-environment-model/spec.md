# shared-environment-model Specification

## Purpose
TBD - created by archiving change replace-alpha-beta-with-test. Update Purpose after archive.
## Requirements
### Requirement: Canonical set of shared environments

The system SHALL define exactly three shared, long-lived environments, each expressed as a protected branch:

- `app` — production.
- `test` — shared non-production.
- `dev` — integration.

There SHALL be no `alpha` or `beta` environment. No `alpha` or `beta` branch is treated as shared infrastructure, and no workflow, template, script, application config, or documentation SHALL reference an `alpha` or `beta` environment.

#### Scenario: Shared set contains exactly app, test, dev

- **WHEN** any workflow, template, or script enumerates the shared / protected environment branches
- **THEN** the set is exactly `app`, `test`, `dev` — and contains neither `alpha` nor `beta`

#### Scenario: No alpha/beta tokens remain
- **WHEN** the repository is searched (case-insensitive, whole-word) for `alpha` or `beta` across workflows, infrastructure templates, CI/deploy scripts, application code, and configuration (excluding archived OpenSpec history)
- **THEN** zero matches referring to a deployment environment remain

### Requirement: test is a multi-region environment

The `test` environment SHALL use the multi-region deployment topology that `beta`/`alpha` previously used: deployed via `master.template` across the primary and secondary regions, with an Aurora Global Cluster and the **non-production** KMS key. `dev` SHALL remain single-region.

#### Scenario: test deploys multi-region
- **WHEN** the deploy workflow runs for the `test` branch
- **THEN** it deploys to both `AWS_REGION_PRIMARY` and `AWS_REGION_SECONDARY` using `master.template`, provisioning an Aurora Global Cluster encrypted with the nonprod KMS key

#### Scenario: Multi-region set is app and test
- **WHEN** the deploy workflow (and `deploy.yml`'s multi-region input) determines which branches deploy multi-region
- **THEN** the multi-region set is exactly `app` and `test` (`dev` deploys single-region)

### Requirement: Branch-to-KMS-scope mapping is unchanged and rule-based

The KMS key selection SHALL remain rule-based: the `app` branch uses the production KMS key; every other branch (including `test` and `dev`) uses the non-production KMS key. This change SHALL NOT enumerate `test` as a special case in the key-selection logic.

#### Scenario: test uses the nonprod key
- **WHEN** the deploy workflow selects the Aurora KMS key for the `test` branch
- **THEN** it reads the nonprod key ARN (`/${dashed-domain}/kms/nonprod/aurora-key-arn`), the same path used for `dev` and feature branches

#### Scenario: app still uses the prod key
- **WHEN** the deploy workflow selects the Aurora KMS key for the `app` branch
- **THEN** it reads the prod key ARN (`/${dashed-domain}/kms/prod/aurora-key-arn`)

### Requirement: Data API HTTP endpoint enabled for shared non-prod environments

The Aurora cluster's Data API HTTP endpoint (`EnableHttpEndpoint`) SHALL be enabled for the shared non-production environments `test` and `dev`, and SHALL NOT reference `beta` or `alpha`.

#### Scenario: Data API enabled for test and dev
- **WHEN** `db.template` evaluates whether to enable the Data API HTTP endpoint
- **THEN** it is enabled when `EnvironmentToImport` is `test` or `dev`, and the condition contains no `beta` or `alpha` term

### Requirement: Brothers homestead reflects the shared set

The brothers tooling SHALL treat `app`, `test`, and `dev` as the homestead (shared) branches — not `alpha` or `beta`.

#### Scenario: Homestead enumeration
- **WHEN** the brothers tooling (`_BrothersCommon.ps1` / `Get-Brothers.ps1`) classifies worktrees
- **THEN** `app`, `test`, and `dev` are the homestead set; `alpha` and `beta` do not appear

