## Context

The AWS infrastructure for this app is layered:

```
bootstrap (per region, single stack today)
  └─ IAM user (GitHubActions), ECR repo, ApiGateway logs role,
     Config rules, S3 bucket policy

env stack per branch (master.template → backend.template → ...)
  └─ SecurityStack (security.template)
        └─ AuroraKmsKey  ← lives here today
        └─ SharedLambdaExecutionRole
  └─ NetworkingStack
  └─ DbStack  ← consumes the KMS key via !GetAtt SecurityStack.Outputs.AuroraKmsKeyArn
  └─ InfrastructureStack
```

For multi-region branches (`app` / `beta` / `alpha`), the deploy workflow deploys this twice — primary in `us-east-1`, secondary in `us-west-2`. The secondary `SecurityStack` creates an `AWS::KMS::ReplicaKey` of the primary's multi-region key.

Two structural consequences of the current layout:

1. **KMS key lifecycle is tied to env stack lifecycle.** When a non-`app` env stack is deleted, the KMS key has `DeletionPolicy: Retain`, so it's left orphaned and continues to bill. Even manual cleanup costs 7 days of `PendingWindowInDays`.
2. **All keys share the same access policy.** The `AllowGitHubActionsUser` statement is on every key, so the CI principal that can deploy `dev` can also operate on the `app` (production) KMS key.

Separately, the secondary AWS region is `us-west-2` purely by historical accident; `us-east-2` is materially cheaper at parity for this workload.

The migration constraint that drives most of the complexity: **Aurora's `KmsKeyId` is immutable after cluster creation.** Any KMS swap requires snapshot-and-restore (or drop-and-recreate).

## Goals / Non-Goals

**Goals:**

- Make Aurora KMS keys outlive any single environment stack, so we never orphan them on stack delete again.
- Separate prod and non-prod KMS access at the IAM/key-policy level (CI cannot destructively touch the prod key).
- Move secondary region from `us-west-2` → `us-east-2` to reduce cost.
- Keep the change reversible up to the point of the region cut-over.

**Non-Goals:**

- **Not** duplicating the entire bootstrap stack (IAM user, ECR, Config rules). Only the KMS key gets split prod/nonprod. Full bootstrap duplication is a much bigger blast radius and not justified by the immediate problem.
- **Not** introducing branch-aware GitHub Actions credentials in this change. The prod KMS key gets a restrictive key policy; whether to also separate the deployer IAM principal is deferred (see Open Questions).
- **Not** preserving the `dev` database content through migration. `dev` is a throwaway environment.
- **Not** touching `src/` application code. This is infra + pipeline only.

## Decisions

### D1. One bootstrap template, `BootstrapScope` parameter, two stack instances per region

Rather than `bootstrap-prod.template` + `bootstrap-nonprod.template`, keep one `bootstrap.template` and deploy it twice per region with `BootstrapScope=prod` and `BootstrapScope=nonprod`. The template uses `BootstrapScope` to:

- Suffix resource names and SSM parameter paths (`/taskmanager/kms/${BootstrapScope}/aurora-key-arn`).
- Switch the KMS key policy via a `Conditions` block (`IsProdScope`).
- Conditionally create the `prod-kms-admin` IAM role only when `IsProdScope`.

The shared bootstrap resources (`GitHubActionsUser`, `WebECRRepository`, `ApiGatewayCloudWatchLogsRole`, Config rules, S3 bucket policy) stay in a **third** stack instance called `bootstrap` (the existing one), with no scope suffix. It does NOT own a KMS key anymore.

Net result per region: three bootstrap stacks — `bootstrap`, `bootstrap-prod`, `bootstrap-nonprod`.

**Alternative considered:** Separate templates per scope. Rejected — 95% template overlap, drift risk, no clarity benefit.

**Alternative considered:** Single `bootstrap` stack with both keys side-by-side, scope encoded in the key alias. Rejected — defeats the "delete the prod KMS stack without affecting nonprod" property.

### D2. KMS key discovery via SSM Parameter Store, not CloudFormation Exports

`bootstrap-prod` / `bootstrap-nonprod` each write their key ARN to a known SSM parameter:

```
/taskmanager/kms/prod/aurora-key-arn      (in each region)
/taskmanager/kms/nonprod/aurora-key-arn   (in each region)
```

Env stacks discover the key by parameter lookup (`{{resolve:ssm:/taskmanager/kms/...}}` in the workflow or via the `AWS::SSM::Parameter::Value<String>` CloudFormation parameter type).

**Why not CloudFormation Exports:** Exports create a hard cross-stack dependency. Once an env stack imports an export, the bootstrap stack cannot be updated in a way that touches the export until every consumer is removed. With dozens of feature branches and `dev`/`alpha`/`beta`/`app` all consuming, the bootstrap stack would become un-updatable.

**Why not a workflow `describe-stacks` lookup:** Also workable, but it means every consumer must hard-code the bootstrap stack name. SSM gives us one canonical, region-aware lookup that's identical from CI and from `aws cloudformation deploy` parameter substitution.

### D3. `bootstrap-prod` KMS key policy: split read/use vs destroy

The `bootstrap-prod` key policy allows three sets of principals different sets of actions:

| Principal                          | Allowed actions                                                                                                                   |
|------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------|
| `rds.amazonaws.com` (service)      | `CreateGrant`, `DescribeKey`, `Decrypt`, `Encrypt`, `GenerateDataKey`, `ReEncryptFrom`, `ReEncryptTo`, `RetireGrant`              |
| GitHub Actions IAM user            | Same set as RDS (so it can wire up grants when creating Aurora clusters), plus `ListGrants`, `RevokeGrant` for cleanup            |
| Account root + `prod-kms-admin` role | Everything (`kms:*`)                                                                                                            |

Notably, GitHub Actions does **not** get `ScheduleKeyDeletion`, `DisableKey`, `PutKeyPolicy`, `DeleteAlias`, `UpdateAlias`, `ReplicateKey` on the prod key. Even if CI credentials leak, the prod key cannot be destroyed by them.

`prod-kms-admin` is created in the same `bootstrap-prod` stack with `AssumeRolePolicyDocument` allowing only `AWS::AccountId:root` — so a human operator with console/SSO access can assume it; the CI user cannot. This deliberately offers no programmatic deletion path.

The `bootstrap-nonprod` key policy is the existing broad policy (CI can do everything) — nonprod must stay friction-free.

### D4. Bootstrap stacks are deployed manually, not from `zbuild.yml`

Bootstrap changes are infrequent (probably once a quarter at most) and need human review — especially `bootstrap-prod`. They will continue to be deployed via the existing manual `aws cloudformation deploy` pattern (already the case for the current `bootstrap` stack; there is no bootstrap job in `zbuild.yml` today).

Tasks document the exact `aws cloudformation deploy` invocations so they're reproducible.

### D5. Migration sequencing: KMS shift first, region shift second

Treat the two halves as sequential phases, even though they ship in the same OpenSpec change:

1. **KMS phase** — all in `us-east-1` (and `us-west-2` while it's still the secondary). Lay down new bootstrap stacks, rewire templates, migrate each env to the new key.
2. **Region phase** — only after KMS phase is fully stable. Tear down `us-west-2`, bring up `us-east-2`.

Doing them at the same time multiplies the failure modes (a KMS bug and a region bug at once is much harder to bisect).

### D6. Per-env migration recipe for the immutable `KmsKeyId`

| Env       | Recipe                                                                                                                                                                                                       |
|-----------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `dev`     | Drop the env stack. Redeploy from CI — empty DB. Acceptable: `dev` is a test env. (Confirm with user — see Open Questions.)                                                                                  |
| feature/* | No action — they import `dev`'s backend (no Aurora of their own), so they pick up the new key on next deploy automatically once `dev` is migrated.                                                            |
| `alpha`, `beta` | Snapshot Aurora cluster → drop env stack → redeploy against new key → restore snapshot into the new cluster. Brief downtime acceptable.                                                                  |
| `app`     | Stand up parallel cluster on new key, restore latest snapshot, dual-write window if needed, cut over DNS + secret rotation, decommission old cluster. Plan for a short maintenance window; coordinate ahead. |

## Risks / Trade-offs

- **[Risk] Prod database unavailable during `app` cluster recreation.** → Mitigate: pre-stage parallel cluster on new key, restore snapshot ahead of time, then do a fast cutover (DNS TTL + secret rotation). Choose a low-traffic window.
- **[Risk] Aurora Global Cluster cannot be in two secondaries simultaneously.** → Mitigate: in Phase 4, fully remove `us-west-2` from the global cluster *before* adding `us-east-2`. The primary in `us-east-1` is read-write throughout.
- **[Risk] Orphaned KMS keys in pre-existing env stacks continue billing forever.** → Mitigate: a one-time `aws kms schedule-key-deletion --pending-window-in-days 7` sweep is added to tasks.md (Phase 5). Lists all keys with the existing `AuroraKmsKey` description pattern.
- **[Risk] SSM parameter overwritten or deleted, breaking env deploys.** → Mitigate: SSM parameters in bootstrap stacks have `DeletionPolicy: Retain`; if accidentally deleted, redeploy the bootstrap stack to recreate. Document in tasks.md.
- **[Risk] SES in `us-east-2` not verified before first `us-east-2` deploy → confirmation emails 5xx.** → Mitigate: Phase 0 prereq creates the SES domain identity + DKIM and verifies before any `us-east-2` workflow run.
- **[Risk] `bootstrap-prod` deployment by a non-privileged user accidentally widens the key policy.** → Mitigate: `bootstrap-prod` is in a separate template instance, and the `prod-kms-admin` role's `AssumeRolePolicyDocument` does not include the CI user; updating it requires the AWS account root or another already-privileged principal.
- **[Trade-off] Three bootstrap stacks per region instead of one** is more operationally visible — three things to keep in sync per region. We accept that in exchange for blast-radius separation.
- **[Trade-off] No CI separation between prod and nonprod deployers in this change** means a compromised GitHub Actions credential can still *deploy* the `app` stack — what it can't do is destroy the prod KMS key. That's a meaningful but incomplete defense. Further hardening is deferred.

## Migration Plan

**Phase 0 — Prereqs (manual, ~1 hour)**
1. Run `aws sesv2 create-email-identity --email-identity appcloud.systems --region us-east-2`; add DKIM CNAMEs to Route 53 hosted zone `Z06422172SASV44F5Y8VA`; wait for verification (`DkimAttributes.Status: SUCCESS`).
2. Run `aws sesv2 put-account-details --production-access-enabled ... --region us-east-2` to exit SES sandbox (24h AWS turnaround).

**Phase 1 — Bootstrap stand-up (no production impact)**
3. Merge template changes: new `bootstrap.template` (with `BootstrapScope` parameter), updated `security.template` (KMS resources still present for now — Phase 5 removes them).
4. Manually deploy in `us-east-1`: `bootstrap-nonprod`, then `bootstrap-prod`.
5. Verify SSM parameters exist and contain valid key ARNs.

**Phase 2 — Non-prod migration**
6. Drop the existing `dev` env stack (`aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`).
7. Trigger CI on `dev` branch; verify new cluster comes up using `bootstrap-nonprod` KMS key.
8. Snapshot existing `alpha` and `beta` Aurora clusters.
9. Drop `alpha` and `beta` env stacks.
10. Redeploy; restore snapshots into new clusters.
11. Trigger redeploys on a couple of representative feature branches to verify their lookup of `dev`'s backend with the new key flow works.

**Phase 3 — Prod migration**
12. Schedule maintenance window with stakeholders.
13. Snapshot `app` Aurora cluster (primary `us-east-1`, secondary `us-west-2`).
14. Stand up parallel Aurora cluster on `bootstrap-prod` key (without flipping DNS yet).
15. Restore latest snapshot to the parallel cluster.
16. Final delta sync (replay any writes that happened during snapshot+restore window).
17. Cut DNS + rotate `taskmanager/database/.../password` secret to point web containers at the new cluster.
18. Verify health, then decommission the old cluster.

**Phase 4 — Region shift (only after Phase 3 is stable for ≥1 week)**
19. Snapshot `app` / `beta` / `alpha` clusters again.
20. Remove `us-west-2` regional cluster from the Aurora Global Cluster (`aws rds remove-from-global-cluster`).
21. Delete `app-appcloud-systems` / `beta-...` / `alpha-...` stacks in `us-west-2`.
22. Deploy `bootstrap`, `bootstrap-nonprod`, `bootstrap-prod` to `us-east-2`.
23. Update `AWS_REGION_SECONDARY: us-east-2` in `zbuild.yml`; merge.
24. Redeploy `alpha` → `beta` → `app` to bring up `us-east-2` secondaries (in that order, so any issue is caught on the lowest-criticality env first).
25. Verify global cluster spans `us-east-1` + `us-east-2`; verify DNS failover health checks updated.

**Phase 5 — Cleanup**
26. Run a one-time `aws kms list-keys` + filter on description matching `Multi-region KMS key for Aurora Global Database encryption - *`; for any not currently referenced by an Aurora cluster, run `aws kms schedule-key-deletion --pending-window-in-days 7`.
27. Final commit: remove `AuroraKmsKey` / `AuroraKmsKeyReplica` resources and `AuroraKmsKeyArn` output from `security.template`.
28. Delete the old `bootstrap` stack in `us-west-2` (now empty of KMS, no remaining purpose).

**Rollback strategies:**
- Phases 1-2: revert the merge; old `security.template` still owns the keys; redeploy.
- Phase 3: restore snapshot to a fresh stack on the old key; old `bootstrap` KMS resources are still live during the migration.
- Phase 4: re-create `us-west-2` stacks from git history; the bootstrap there was preserved until Phase 5.
- Phase 5 is the point of no return for both halves.

## Open Questions

1. **Separate prod-deployer GitHub Actions credentials?** The current design uses one IAM user with a restrictive prod key policy. A second IAM user (with `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` GitHub secrets) used only for `app`-branch deploys would tighten things further. Recommended: defer to a follow-up change. Confirm.
2. **`dev` data loss tolerance.** Confirm `dev`'s database can be wiped during migration. If not, snapshot/restore for `dev` too.
3. **Single change or two?** This proposal documents one change with sequential phases. An alternative is to split into two OpenSpec changes (one for KMS, one for region). Recommended: one change, two phases — they share migration mechanics and the spec language is intertwined.
4. **Existing orphaned keys.** Is the user already aware of orphaned KMS keys from previously deleted env stacks, and is the Phase 5 sweep welcome? Or should we leave those for a separate cleanup ticket?
5. **`bootstrap-prod` deployment access today.** Who currently has AWS credentials capable of deploying `bootstrap-prod` and creating IAM roles? If only the CI user does, the chicken-and-egg problem (CI cannot deploy `bootstrap-prod` because the resulting role excludes CI) needs to be resolved by either a one-time deploy from the account root user, or temporarily relaxing the trust policy.
