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
2. **All keys share the same access policy, and the same CI principal can touch all of them.** The `AllowGitHubActionsUser` statement is on every key; the `app` deploy job runs as the same IAM user as the `dev` deploy job.

The migration constraint that drives most of the complexity: **Aurora's `KmsKeyId` is immutable after cluster creation.** Any KMS swap requires snapshot-and-restore (or drop-and-recreate).

This change covers only the KMS centralization plus the CI credential split. The secondary-region migration (us-west-2 → us-east-2) is split into a sibling change [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md) that lands after this one. During this change the secondary region remains `us-west-2`, and the new bootstrap stacks are deployed to both `us-east-1` and `us-west-2`.

## Goals / Non-Goals

**Goals:**

- Make Aurora KMS keys outlive any single environment stack, so we never orphan them on stack delete again.
- Separate prod and non-prod KMS access at the IAM/key-policy level (CI for non-prod cannot destructively touch the prod key, and the prod-deployer CI principal cannot reach non-prod resources).
- Establish a clean foundation for the follow-on region change to ride on.
- Keep the change reversible up to the prod cluster cutover (Phase 4).

**Non-Goals:**

- **Not** changing the secondary AWS region. us-west-2 stays. (Sibling change.)
- **Not** duplicating the entire bootstrap stack (ECR, Config rules). Only the KMS key and the `GitHubActionsUserProd` IAM user get prod/nonprod separation. The shared resources (ECR repo, Config rules, S3 bucket policy, ApiGateway logs role) stay in the existing `bootstrap` stack.
- **Not** preserving the `dev` database content through migration (resolved as drop-and-recreate; see Resolved Decisions below).
- **Not** touching `src/` application code. Infra + pipeline only.

## Decisions

### D1. One bootstrap template, `BootstrapScope` parameter, three stack instances per region

Rather than `bootstrap-prod.template` + `bootstrap-nonprod.template`, keep one `bootstrap.template` and deploy it with `BootstrapScope=prod` and `BootstrapScope=nonprod` as separate stack instances. The template uses `BootstrapScope` to:

- Suffix resource names and SSM parameter paths (`/taskmanager/kms/${BootstrapScope}/aurora-key-arn`).
- Switch the KMS key policy via a `Conditions` block (`IsProdScope`).
- Conditionally create the `prod-kms-admin` IAM role and `GitHubActionsUserProd` IAM user only when `IsProdScope`.

The shared bootstrap resources (`GitHubActionsUser` for non-prod, `WebECRRepository`, `ApiGatewayCloudWatchLogsRole`, Config rules, S3 bucket policy) stay in the existing unscoped `bootstrap` stack instance, with no scope suffix.

Net result per region: three bootstrap stacks — `bootstrap`, `bootstrap-prod`, `bootstrap-nonprod`.

**Alternative considered:** Separate templates per scope — rejected, 95% overlap, drift risk.

**Alternative considered:** Single `bootstrap` stack with both keys side-by-side — rejected, defeats the "delete prod KMS stack independently of nonprod" property.

### D2. KMS key discovery via SSM Parameter Store, not CloudFormation Exports

`bootstrap-prod` / `bootstrap-nonprod` each write their key ARN to a known SSM parameter:

```
/taskmanager/kms/prod/aurora-key-arn      (in each region)
/taskmanager/kms/nonprod/aurora-key-arn   (in each region)
```

Env stacks discover the key by parameter lookup at deploy time (workflow `aws ssm get-parameter` call, then passed as a CloudFormation parameter).

**Why not CloudFormation Exports:** Exports create a hard cross-stack dependency. Once an env stack imports an export, the bootstrap stack cannot be updated in a way that touches the export until every consumer is removed. With dozens of feature branches plus `dev`/`alpha`/`beta`/`app` all consuming, the bootstrap stack would become un-updatable.

**Why not a workflow `describe-stacks` lookup:** Also workable, but it means every consumer must hard-code the bootstrap stack name. SSM gives us one canonical, region-aware lookup.

### D3. `bootstrap-prod` KMS key policy: split read/use vs destroy

The `bootstrap-prod` key policy allows three sets of principals different sets of actions:

| Principal                          | Allowed actions                                                                                                                   |
|------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------|
| `rds.amazonaws.com` (service)      | `CreateGrant`, `DescribeKey`, `Decrypt`, `Encrypt`, `GenerateDataKey`, `ReEncryptFrom`, `ReEncryptTo`, `RetireGrant`              |
| `GitHubActionsUserProd` IAM user   | Same set as RDS (so it can wire up grants when creating Aurora clusters), plus `ListGrants`, `RevokeGrant` for cleanup            |
| Account root + `prod-kms-admin` role | Everything (`kms:*`)                                                                                                            |

Notably, `GitHubActionsUserProd` does **not** get `ScheduleKeyDeletion`, `DisableKey`, `PutKeyPolicy`, `DeleteAlias`, `UpdateAlias`, `ReplicateKey` on the prod key. Even if the prod CI credentials leak, the prod key cannot be destroyed by them. The existing non-prod CI user is not on the prod key policy at all.

`prod-kms-admin` is created in the same `bootstrap-prod` stack with `AssumeRolePolicyDocument` allowing only `AWS::AccountId:root` — so a human operator with console/SSO access can assume it; no CI user can.

The `bootstrap-nonprod` key policy is the existing broad policy (CI can do everything) — nonprod must stay friction-free.

### D4. Separate `GitHubActionsUserProd` IAM user, branch-conditional credentials in zbuild.yml

`bootstrap-prod` creates `GitHubActionsUserProd` — a new IAM user with its own access key pair and its own `DeploymentPolicy-prod` policy that mirrors the shape of the existing `DeploymentPolicy` but is scoped to the resources needed for the `app` env stack.

`zbuild.yml`'s deploy job adds a step that resolves the credential set:

```yaml
- name: Select AWS credentials
  id: select-creds
  run: |
    if [ "${{ needs.get-branch-name.outputs.branch-name }}" = "app" ]; then
      echo "access-key-id=${{ secrets.AWS_ACCESS_KEY_ID_PROD }}" >> $GITHUB_OUTPUT
      echo "secret-access-key=${{ secrets.AWS_SECRET_ACCESS_KEY_PROD }}" >> $GITHUB_OUTPUT
    else
      echo "access-key-id=${{ secrets.AWS_ACCESS_KEY_ID }}" >> $GITHUB_OUTPUT
      echo "secret-access-key=${{ secrets.AWS_SECRET_ACCESS_KEY }}" >> $GITHUB_OUTPUT
    fi

- name: Configure AWS credentials
  uses: aws-actions/configure-aws-credentials@v4
  with:
    aws-access-key-id: ${{ steps.select-creds.outputs.access-key-id }}
    aws-secret-access-key: ${{ steps.select-creds.outputs.secret-access-key }}
    aws-region: ${{ matrix.region }}
```

GitHub does **not** mask secrets written to `$GITHUB_OUTPUT` automatically; we work around that by passing the credentials through GitHub's secrets-only `aws-actions/configure-aws-credentials` action, which redacts them in subsequent shell steps. Alternative considered: a job-level matrix include that picks the secret-name string and uses `${{ secrets[<expression>] }}` indirection — rejected because `secrets` cannot be indexed by an expression in current GitHub Actions syntax.

**Note on the cleanup-on-branch-delete workflow:** it operates only on feature branches (protected against `app`/`beta`/`alpha`/`dev`), so it should continue to use the existing non-prod credentials. No conditional logic needed there.

### D5. Bootstrap stacks are deployed manually, not from `zbuild.yml`

Bootstrap changes are infrequent and need human review — especially `bootstrap-prod`. They continue to be deployed via the existing manual `aws cloudformation deploy` pattern. Tasks.md documents the exact invocations.

### D6. Per-env migration recipe for the immutable `KmsKeyId`

| Env       | Recipe                                                                                                                                                                                                       |
|-----------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `dev`     | Drop the env stack. Redeploy from CI — empty DB. (Resolved: see Resolved Decisions Q2.)                                                                                                                       |
| feature/* | No action — they import `dev`'s backend (no Aurora of their own), so they pick up the new key on next deploy automatically once the application.template wiring is in.                                       |
| `alpha`, `beta` | Snapshot Aurora cluster → drop env stack → redeploy against new key → restore snapshot into the new cluster. Brief downtime acceptable.                                                                  |
| `app`     | Stand up parallel cluster on new key, restore latest snapshot, brief maintenance window, cut over secret + DNS, decommission old cluster.                                                                    |

## Risks / Trade-offs

- **[Risk] Prod database unavailable during `app` cluster recreation.** → Mitigate: pre-stage parallel cluster on new key, restore snapshot ahead of time, then do a fast cutover. Choose a low-traffic window.
- **[Risk] Orphaned KMS keys in pre-existing env stacks continue billing forever.** → Mitigate: one-time `aws kms schedule-key-deletion --pending-window-in-days 7` sweep in Phase 5 (Resolved Decisions Q4).
- **[Risk] SSM parameter overwritten or deleted, breaking env deploys.** → Mitigate: SSM parameters in bootstrap stacks have `DeletionPolicy: Retain`; if accidentally deleted, redeploy bootstrap to recreate.
- **[Risk] `bootstrap-prod` deployment by a non-privileged user accidentally widens the key policy.** → Mitigate: `bootstrap-prod` is in a separate stack instance, deployed by a human admin user (Resolved Decisions Q5); CI cannot update it.
- **[Risk] `GitHubActionsUserProd` access key leaks before key rotation policy is in place.** → Mitigate: same deploy/rotation policy as the existing CI user; access key is created in CloudFormation but its secret value never appears in `$GITHUB_STEP_SUMMARY` — the operator copies it once from the stack output into GitHub secrets. Tasks.md mandates rotating the key after the initial setup.
- **[Trade-off] Three bootstrap stacks per region instead of one** is more operationally visible — three things to keep in sync per region. We accept that for blast-radius separation.
- **[Trade-off] Branch-conditional credentials add one moving part to the workflow.** If the `app` branch's prod secrets are missing or invalid, the deploy fails fast at the `configure-aws-credentials` step rather than silently falling back to non-prod credentials. The workflow explicitly errors if `secrets.AWS_ACCESS_KEY_ID_PROD == ''` on an `app` push.

## Migration Plan

**Phase 0 — Prereqs (manual, ~30 min)**
1. Identify which existing human admin IAM user will deploy `bootstrap-prod` for the first time (Resolved Decisions Q5).
2. Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` for the Phase 5 sweep.

**Phase 1 — Bootstrap stand-up (no production impact)**
3. Merge template changes: new `bootstrap.template` (with `BootstrapScope` parameter), updated `security.template` (KMS resources still present for now — Phase 5 removes them).
4. Human admin deploys `bootstrap-nonprod` in `us-east-1` and `us-west-2`.
5. Human admin deploys `bootstrap-prod` in `us-east-1` and `us-west-2`. Captures the `GitHubActionsUserProd` access key and secret from the stack outputs.
6. Add `AWS_ACCESS_KEY_ID_PROD` and `AWS_SECRET_ACCESS_KEY_PROD` to GitHub repository secrets.
7. Verify SSM parameters exist and contain valid key ARNs in both regions.

**Phase 2 — Wire workflows and templates**
8. Merge workflow + template changes (branch-conditional credentials, SSM lookup, application.template KmsKeyArn parameter, backend.template rewiring, master.template KmsKeyArn required).
9. Push to a throwaway feature branch and verify the deploy succeeds using the non-prod CI user and the nonprod KMS key.

**Phase 3 — Non-prod env migration**
10. Drop `dev` env stack; redeploy empty.
11. Snapshot `alpha`/`beta`; drop env stacks; redeploy; restore snapshots.
12. Redeploy two representative feature branches to verify.

**Phase 4 — Prod migration (`app`)**
13. Schedule maintenance window.
14. Snapshot `app` cluster (us-east-1 and us-west-2).
15. Stand up parallel cluster on new prod key.
16. Restore snapshot.
17. Cut DNS + rotate secret.
18. Decommission old cluster.

**Phase 5 — Cleanup**
19. Schedule deletion of pre-existing orphaned `AuroraKmsKey` resources (Phase 0 inventory).
20. Final commit: remove `AuroraKmsKey` / `AuroraKmsKeyReplica` from `security.template`.
21. Rotate `GitHubActionsUserProd` access key once (security hygiene; it was visible in the stack output during Phase 1).

**Rollback strategies:**
- Phases 1-2: revert the merge; old `security.template` still owns the keys; redeploy.
- Phase 3: restore snapshot to a fresh stack on the old key.
- Phase 4: restore snapshot to a fresh stack on the old key (old `bootstrap` KMS resources still live).
- Phase 5 is the point of no return.

## Resolved Decisions

The original draft of this change carried five open questions. They were resolved as follows:

- **Q1 — Separate prod-deployer GitHub Actions credentials?** **YES, in this change.** Added `GitHubActionsUserProd` IAM user in `bootstrap-prod` and branch-conditional credential selection in `zbuild.yml` (see D4). Increases scope by ~one workflow step, one IAM user, one IAM policy, and two new GitHub secrets, but gives clean prod/nonprod CI separation now.
- **Q2 — `dev` data loss tolerance?** **OK to drop and recreate empty.** `dev` is a test env (per CLAUDE.md); no snapshot/restore needed. Saves several steps in Phase 3.
- **Q3 — Single change or two?** **Split.** The us-west-2 → us-east-2 migration is the sibling change [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md). That change depends on this one landing first (it builds on the `bootstrap-prod`/`bootstrap-nonprod` pattern).
- **Q4 — Existing orphaned keys.** **Yes, sweep in Phase 5.** Inventory in Phase 0 task 1.2; schedule deletion in Phase 5 task 7.1.
- **Q5 — First `bootstrap-prod` deploy.** **Existing human admin IAM user.** Identify which user in Phase 0 task 1.1; that user runs the manual `aws cloudformation deploy` for `bootstrap-prod` in both regions during Phase 1. No chicken-and-egg: the admin user already has AdministratorAccess (or equivalent) independent of anything `bootstrap-prod` creates.
