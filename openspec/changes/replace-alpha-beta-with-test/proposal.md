## Why

The deployment model carries three shared non-production-and-production environments expressed as long-lived branches — `app` (prod), plus **two** near-identical shared non-prod environments `beta` and `alpha` — alongside single-region `dev`. `beta` and `alpha` are redundant: both are multi-region shared-infrastructure branches with the same topology (Aurora Global Cluster, nonprod KMS key, same template path), differing only in name. Maintaining two of them doubles the standing AWS cost (two extra multi-region Aurora Global Clusters) and the operational surface (two extra stacks to deploy, teardown, and reason about) for no distinct purpose. Collapsing them into a single shared non-prod environment named `test` simplifies the branch model, halves the redundant non-prod infrastructure, and removes a recurring source of confusion in workflow/branch logic and docs.

## What Changes

- **BREAKING** Remove the `beta` and `alpha` shared environments entirely and introduce a single shared non-production environment **`test`**. The shared-infrastructure branch set becomes **`app` + `test` + `dev`** (was `app` + `beta` + `alpha` + `dev`).
- **BREAKING** Multi-region deployment now applies to **`app` + `test`** (was `app` + `beta` + `alpha`); `dev` stays single-region. `test` uses the same multi-region topology (Aurora Global Cluster, nonprod KMS key, `master.template`) that `beta`/`alpha` used.
- Deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml), [.github/workflows/deploy.yml](../../../.github/workflows/deploy.yml)): replace the `beta`/`alpha` branch tests and the `"app beta alpha"` / `"app beta alpha dev"` workflow_call input defaults with the `test`-based sets.
- Branch-delete cleanup ([.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml)): protected-branch list `app/beta/alpha/dev` → `app/test/dev`.
- Infrastructure: `db.template`'s `EnvironmentToImport` condition that enables the Data API HTTP endpoint for `beta`/`alpha` (and dev) → keyed on `test` (and dev); `master.template` / `application.template` multi-region descriptions updated.
- Brothers tooling ([scripts/_BrothersCommon.ps1](../../../scripts/_BrothersCommon.ps1), [scripts/Get-Brothers.ps1](../../../scripts/Get-Brothers.ps1), [scripts/merge.sh](../../../scripts/merge.sh)): homestead set `app/beta/alpha/dev` → `app/test/dev`.
- Docs: [CLAUDE.md](../../../CLAUDE.md) and [BROTHERS.md](../../../BROTHERS.md) — branch model, multi-region branch list, and homestead references.
- **Rollout/decommission:** the existing deployed `beta` and `alpha` stacks (env stacks in both regions + their Aurora Global Clusters) are torn down as part of this change, and a `test` shared environment is stood up. Teardown uses the `robust-aurora-cluster-teardown` mechanism (now global-cluster-aware).

## Capabilities

### New Capabilities
- `shared-environment-model`: defines the canonical set of shared, long-lived environments (production `app`, shared non-prod `test`, integration `dev`), which are multi-region vs single-region, and the branch→environment / branch→KMS-scope mapping — with `test` as the single shared non-prod environment replacing `beta` and `alpha`.

### Modified Capabilities
- `branch-stack-cleanup`: the protected (never-auto-deleted) branch set changes from `app`/`beta`/`alpha`/`dev` to `app`/`test`/`dev`.

## Impact

- **Workflows**: `zbuild.yml`, `deploy.yml`, `cleanup-on-branch-delete.yml` (branch/env logic only).
- **Infrastructure**: `db.template`, `master.template`, `application.template` (env-conditional logic + descriptions). No change to the domain-derived naming or KMS key *mechanism* (the `app`=prod / every-other-branch=nonprod rule in `aurora-kms-key-management` is already name-agnostic and is unaffected).
- **Scripts/tooling**: brothers homestead enumeration; `merge.sh` shared-branch guard.
- **Docs**: `CLAUDE.md`, `BROTHERS.md`.
- **Live AWS**: decommission `beta` + `alpha` env stacks and Aurora Global Clusters (both regions); provision the `test` shared environment.
- **GitVersion** ([GitVersion.yml](../../../GitVersion.yml)): the `release`/`hotfix` branches carry semver *prerelease labels* literally named `alpha`/`beta`. These are version-string labels, not the deployment environments — see design for whether they are in scope.
- **Out of scope**: renaming `app`→`prod` or `dev` (the production and integration environments keep their names); GitVersion semver-label decision is called out separately in design.
