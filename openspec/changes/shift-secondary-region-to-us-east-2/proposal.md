## Why

The platform's secondary AWS region is `us-west-2` purely by historical accident. `us-east-2` is materially cheaper at parity for the same workload (compute and data transfer, especially cross-region replication back to `us-east-1`), and there is no architectural reason — latency, compliance, customer locality — that requires us to be on the west coast.

This change migrates the secondary region from `us-west-2` to `us-east-2` for the `app`/`beta`/`alpha` multi-region branches (the only ones that deploy to a secondary region). Single-region branches (`dev`, feature branches) are unaffected.

**Dependency:** This change depends on [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md) landing first. That change establishes the `bootstrap-prod` / `bootstrap-nonprod` stacks that own the Aurora KMS keys; this change re-deploys those stacks to `us-east-2` and tears down their `us-west-2` instances. The order matters because the KMS replicas must exist in the new secondary region before Aurora regional clusters can be created there.

## What Changes

- **BREAKING** `AWS_REGION_SECONDARY` in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) changes from `us-west-2` to `us-east-2`. The deploy matrix's secondary-region entry changes accordingly. No `us-west-2` literal remains in the workflow file.
- **BREAKING** Tear down all `us-west-2` stacks: `app-appcloud-systems`, `beta-appcloud-systems`, `alpha-appcloud-systems`, and (in Phase 4 cleanup) `bootstrap`, `bootstrap-prod`, `bootstrap-nonprod`.
- Deploy `bootstrap`, `bootstrap-prod`, `bootstrap-nonprod` to `us-east-2` so that the new secondary region has the prerequisite KMS replicas, ECR repository, IAM role for API Gateway logs, and Config rules in place before any env stack is laid down.
- Re-create the Aurora Global Cluster secondary regional clusters in `us-east-2` for `app`, `beta`, `alpha` (the global cluster reference itself stays; only its membership changes).
- Re-create DNS health checks and Route 53 failover records to point at the new `us-east-2` ALBs.
- Verify SES domain identity (`appcloud.systems`) and DKIM in `us-east-2`, mirroring the existing per-region setup documented in [CLAUDE.md](../../../CLAUDE.md). Submit SES production-access request for `us-east-2`.

## Capabilities

### New Capabilities

- `multi-region-deployment-topology`: Which AWS regions the platform deploys to as primary and secondary, which branches deploy to the secondary region, how the Aurora Global Cluster membership is constrained, and the SES + DNS prerequisites for the secondary region.

### Modified Capabilities

_None._ The KMS spec from the prior change is region-agnostic ("secondary region" is a variable, not literal `us-west-2` or `us-east-2`), so no delta is needed there.

## Impact

**Workflows:**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — `AWS_REGION_SECONDARY: us-east-2`; matrix `region: us-east-2`; no `us-west-2` literal remains.

**Infrastructure (re-deployed, not edited):**
- `bootstrap`, `bootstrap-prod`, `bootstrap-nonprod` stacks: existing in `us-east-1`, **stand up in `us-east-2`** (deployed manually by the human admin per the prior change's pattern), **delete in `us-west-2`** at the end of this change.
- `app`/`beta`/`alpha` env stacks: existing in `us-east-1`, **stand up in `us-east-2`** by triggering the deploy workflow, **delete in `us-west-2`** before the workflow change merges (so the workflow doesn't try to redeploy them there).

**External dependencies:**
- AWS SES: domain identity + DKIM + production-access in `us-east-2`. The DKIM CNAMEs added to Route 53 hosted zone `Z06422172SASV44F5Y8VA`. Required before any `us-east-2` deploy runs.
- AWS Route 53: DNS failover records and health checks moved to target the new `us-east-2` ALBs.
- Aurora Global Cluster: `taskmanager-${BranchName}-global-cluster` for `app`/`beta`/`alpha` — `us-west-2` regional cluster removed via `aws rds remove-from-global-cluster`, `us-east-2` regional cluster added by the workflow during deploy.

**Existing data (migration required, data-preserving):**
- `app` Aurora cluster: snapshot in `us-east-1` (primary); new `us-east-2` secondary catches up via Aurora Global Database replication once added. No data loss.
- `alpha`/`beta` Aurora clusters: same approach.

**Documentation:**
- [CLAUDE.md](../../../CLAUDE.md) — region references updated; SES verification list extended to include `us-east-2`; replace `us-west-2` mentions with `us-east-2`.
- [BRANCH_MANAGEMENT_README.md](../../../BRANCH_MANAGEMENT_README.md) — region references updated.

**Out of scope (explicitly):**
- No change to KMS topology, key policy, bootstrap stack content (other than the deploy target region). Those land in the prior change.
- No change to single-region branches' behaviour (`dev`, feature branches still single-region in `us-east-1`).
- No change to application code in `src/`.
