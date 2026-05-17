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

The change also takes a deliberate **clean-slate** approach to the bootstrap layout: the existing single `bootstrap` stack per region is torn down rather than updated in place. This frees us from compatibility constraints, lets each template have a single clear purpose, and avoids drift surprises (the existing `bootstrap` stack contains at least one resource — `GoogleOAuthSecrets` — that no longer appears in the template). The accepted consequences of teardown are spelled out in the proposal and revisited in Risks below.

## Goals / Non-Goals

**Goals:**

- Make Aurora KMS keys outlive any single environment stack, so we never orphan them on stack delete again.
- Separate prod and non-prod KMS access at the IAM/key-policy level (CI for non-prod cannot destructively touch the prod key, and the prod-deployer CI principal cannot reach non-prod resources).
- Establish a clean foundation for the follow-on region change to ride on.
- Keep the change reversible up to the prod cluster cutover (Phase 4).

**Non-Goals:**

- **Not** changing the secondary AWS region. us-west-2 stays. (Sibling change.)
- **Not** splitting the ECR repository into prod and nonprod. The web container ECR repo stays single and shared — both `bootstrap-prod` and `bootstrap-nonprod` IAM users get push/pull on the same repo. (Splitting ECR for stricter image isolation is a possible future change, but is not driven by this one.)
- **Not** preserving the existing `bootstrap` stack content. The old stack is torn down; relevant resources are re-created in `bootstrap-shared` / `bootstrap-prod` / `bootstrap-nonprod` from scratch. Consequences (ECR rebuild, GitHub-secret rotation, brief deploy quiet window) are explicitly accepted.
- **Not** preserving the `dev` database content through migration (resolved as drop-and-recreate; see Resolved Decisions below).
- **Not** touching `src/` application code. Infra + pipeline only.

## Decisions

### D1. Two templates, three stack instances per region

Split the bootstrap layer into two files:

- **`infrastructure/bootstrap-shared.template`** (new file) — account-wide infrastructure that has no prod/nonprod distinction:
  - `WebECRRepository` (with an explicit `RepositoryName: !Sub "ecr-${AWS::AccountId}-${AWS::Region}"` so the workflow's hardcoded image URI stays valid across the recreation).
  - `ApiGatewayCloudWatchLogsRole` + `ApiGatewayAccount`.
  - `LogRetentionConfigFunction` + `ConfigRuleRole` + `LogRetentionConfigRule` + `LogRetentionRemediationDocument` + `SSMAutomationRole` + `LogRetentionRemediation` + `ConfigInvokeLambdaPermission`.
  - `TemplatesBucketPolicy` on the externally-managed `cf-templates-{account}-{region}` bucket.
  - Deployed once per region as stack name **`bootstrap-shared`**.

- **`infrastructure/bootstrap.template`** (rewritten from scratch) — per-scope KMS + IAM:
  - **One parameter only:** `PrimaryKeyArn` (`Default: ""`). Empty (default) means this is the primary-region deploy; non-empty means this is the secondary-region replica deploy with the given primary key ARN. Scope (prod vs nonprod) is derived from the **stack name** via a substring check (`!Join ["", !Split ["nonprod", StackName]] != StackName`). No `BootstrapScope` parameter, no `IsPrimaryRegion` parameter.
  - Conditions: `IsProd`, `IsNonprod`, `IsPrimary`, `IsReplica`, `IsProdPrimary`, `IsNonprodPrimary` — all derived from `AWS::StackName` and `PrimaryKeyArn`.
  - Resources: `AuroraKmsKey` (when `IsPrimary`) / `AuroraKmsKeyReplica` (when `IsReplica`), `AWS::KMS::Alias` `taskmanager-aurora-{nonprod|prod}` (selected via `!If [IsNonprod, ...]`), `AWS::SSM::Parameter` `/taskmanager/kms/{nonprod|prod}/aurora-key-arn`, the scope's IAM user (`GitHubActionsUser` when `IsNonprodPrimary`, `GitHubActionsUserProd` when `IsProdPrimary`) + `AWS::IAM::AccessKey` + scope-named `DeploymentPolicy`, and (only when `IsProdPrimary`) the `prod-kms-admin` IAM role.
  - Deployed once per scope per region as stack name **`bootstrap-prod`** or **`bootstrap-nonprod`**. Primary-region deploys take no `--parameter-overrides` at all.

  **Why scope-from-stack-name instead of a `BootstrapScope` parameter:** the stack name was always the authoritative identifier (you couldn't run `bootstrap-prod` with `BootstrapScope=nonprod` and have the change be meaningful). Making the parameter explicit just doubled the source of truth and created a foot-gun if the two ever disagreed. The substring check (`Join("", Split("nonprod", S)) != S`) is the CFN workaround for the missing `Fn::Contains` intrinsic.

Net result per region: three stack instances from two template files — `bootstrap-shared`, `bootstrap-prod`, `bootstrap-nonprod`. The original single `bootstrap` stack and template content is fully retired.

**Alternative considered:** Single template with three scope values (`shared`, `prod`, `nonprod`) and per-resource `Conditions`. Rejected — every resource sprouts a `Condition:` line, and the file becomes a giant `if/else` ladder. Two files are clearer.

**Alternative considered:** Keep the original `bootstrap` stack updated in place and only add `bootstrap-prod` / `bootstrap-nonprod` alongside it (the original approach). Rejected after the user opted to accept teardown — the in-place approach required gating every existing resource with a backwards-compat `Condition: IsUnscoped`, and a no-op changeset against the live stack surfaced unrelated pre-existing drift that complicated the deploy story.

**Alternative considered:** Put `WebECRRepository` in `bootstrap-prod` and `bootstrap-nonprod` (two separate ECR repos for image isolation). Rejected as a non-goal of this change — the existing CI image-tag content-addressing assumes one ECR per region. Splitting ECR is left as a possible follow-up.

**Note on the `GitHubActionsUser` move:** the non-prod CI user moves from the legacy `bootstrap` stack to `bootstrap-nonprod`. This means the access key is reissued — the existing `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets must be updated from the new stack output before any non-prod CI deploy will succeed against the new layout. Treat this rotation as a hard part of the cut-over, not an afterthought.

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

Notably, `GitHubActionsUserProd` does **not** get `ScheduleKeyDeletion`, `DisableKey`, `PutKeyPolicy`, `DeleteAlias`, `UpdateAlias`, `ReplicateKey` on the prod key. Even if the prod CI credentials leak, the prod key cannot be destroyed by them.

**`AllowAccountRoot` is paired with a `NotPrincipal` default-deny.** Without this, the standard `AllowAccountRoot kms:* Resource:*` statement delegates evaluation to IAM, and any IAM user in the account whose own IAM policy grants `kms:*` (e.g. the broad legacy `DeploymentPolicy` that `GitHubActionsUser` still carries) can reach the prod key — including decrypting prod data. To close that gap, the prod key policy includes a final `Effect: Deny` statement with `NotPrincipal` listing only the authorized four (root, `GitHubActionsUserProd`, `prod-kms-admin`, `rds.amazonaws.com`). Anything not in that list is explicitly denied, overriding the IAM-delegation pathway. (Discovered during T3.C of the access-control validation — nonprod CI was able to `DescribeKey` on the prod key until the Deny was added.)

**`KeyAdminPrincipalArn` parameter exists to handle KMS lockout protection during setup.** When AWS KMS applies a key policy update, it runs a lockout-safety check: the calling principal must still be able to call `kms:PutKeyPolicy` under the new policy. Our Deny statement covers `kms:*`, so the deployer (typically an IAM user like `developer-tim`) is denied along with everyone else not in the NotPrincipal exemption — and the deploy fails with *"The new key policy will not allow you to update the key policy in the future."* The `KeyAdminPrincipalArn` parameter (optional, default empty) adds one more principal to the NotPrincipal list, intended for the deploying user during initial setup. The long-term path is for the deployer to assume `prod-kms-admin` (which is already in the exemption) and run deploys from that role, at which point this parameter can be left empty. (Discovered when applying the NotPrincipal Deny in the first place — the update was rejected by KMS's lockout check.)

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
- **[Risk] SSM parameter overwritten or deleted, breaking env deploys.** → Mitigate: if accidentally deleted, redeploy bootstrap to recreate (it's a one-line CFN resource). The parameter intentionally does NOT have `DeletionPolicy: Retain` during setup/iteration — Retain plus a fixed parameter name turns every failed deploy into an "AlreadyExists" blocker requiring manual cleanup before retry. Add Retain back in a follow-up once iteration settles down and a fixed Retain-protected parameter is more valuable than easy retries. The same applies to the KMS keys themselves (no Retain during setup; add Retain once databases are actually encrypting data with them).
- **[Risk] `bootstrap-prod` deployment by a non-privileged user accidentally widens the key policy.** → Mitigate: `bootstrap-prod` is in a separate stack instance, deployed by a human admin user (Resolved Decisions Q5); CI cannot update it.
- **[Risk] `GitHubActionsUserProd` access key leaks before key rotation policy is in place.** → Mitigate: same deploy/rotation policy as the existing CI user; access key is created in CloudFormation but its secret value never appears in `$GITHUB_STEP_SUMMARY` — the operator copies it once from the stack output into GitHub secrets. Tasks.md mandates rotating the key after the initial setup.
- **[Trade-off] Three bootstrap stacks per region instead of one** is more operationally visible — three things to keep in sync per region. We accept that for blast-radius separation.
- **[Trade-off] Branch-conditional credentials add one moving part to the workflow.** If the `app` branch's prod secrets are missing or invalid, the deploy fails fast at the `configure-aws-credentials` step rather than silently falling back to non-prod credentials. The workflow explicitly errors if `secrets.AWS_ACCESS_KEY_ID_PROD == ''` on an `app` push.
- **[Risk] Teardown of the existing `bootstrap` stack briefly removes `TemplatesBucketPolicy`.** During that gap, any CI deploy attempting to upload a packaged template to the S3 bucket will fail. → Mitigate: schedule the teardown for a deploy quiet window (no in-flight PRs, no pending merges), and stand `bootstrap-shared` up immediately after the delete completes.
- **[Risk] Reissuing both non-prod and prod CI access keys at the same time risks an "all CI broken" window if the operator forgets to update GitHub secrets.** → Mitigate: the tasks list both secret updates as required gates before the next CI run; the `bootstrap-shared` / `bootstrap-nonprod` / `bootstrap-prod` stacks all expose their new access keys as `NoEcho: true` outputs the operator can copy once.
- **[Trade-off] Loss of one ECR image cache.** When `WebECRRepository` is recreated by `bootstrap-shared`, all existing image tags are gone. The next CI deploy on any branch triggers a full Docker rebuild (~3 minutes per region). Subsequent deploys benefit from the new ECR's empty-then-populated cache normally.

## Migration Plan

**Phase 0 — Prereqs (manual, ~30 min)**
1. Identify which existing human admin IAM user will deploy `bootstrap-prod` for the first time (Resolved Decisions Q5).
2. Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` for the Phase 5 sweep.

**Phase 1 — Bootstrap teardown and stand-up (deploy quiet window required)**

This phase replaces the legacy `bootstrap` stack with three new stacks. Schedule a deploy quiet window — no in-flight PRs, no pending merges — because the `TemplatesBucketPolicy` is briefly absent between the teardown and `bootstrap-shared` coming up.

3. Merge template changes to the working branch: new `bootstrap-shared.template`, rewritten `bootstrap.template`, updated `security.template` (KMS resources still present for now — Phase 5 removes them).
4. Quiet window opens. Human admin deletes the legacy stack: `aws cloudformation delete-stack --stack-name bootstrap --region us-east-1` and `--region us-west-2`. Wait for both `DELETE_COMPLETE`.
5. Human admin deploys `bootstrap-shared` in `us-east-1` and `us-west-2`.
6. Human admin deploys `bootstrap-nonprod` in `us-east-1` (primary) and `us-west-2` (replica). Captures the new `GitHubActionsUser` access key + secret from `us-east-1` stack outputs.
7. Human admin deploys `bootstrap-prod` in `us-east-1` (primary) and `us-west-2` (replica). Captures the `GitHubActionsUserProd` access key + secret from `us-east-1` stack outputs.
8. Update GitHub repository secrets: rotate `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` (now from `bootstrap-nonprod`), add new `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` (from `bootstrap-prod`).
9. Verify SSM parameters exist and contain valid key ARNs in both regions.
10. Quiet window closes — normal CI resumes.

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
- Phase 1: the legacy `bootstrap` stack is gone after step 4 — there is no clean revert to the old layout. Recovery means re-deploying the old `bootstrap.template` content from git history as a new stack and rotating GitHub secrets back. This is mechanical but slow (~30 min). Plan the quiet window for a time when rolling forward is the only realistic option.
- Phase 2: revert the workflow / template merge; the old `security.template` still owns the per-env KMS keys; existing env stacks continue using them. (`bootstrap-prod`/`bootstrap-nonprod` keys are unused but harmless.)
- Phase 3: restore snapshot to a fresh stack on the old key.
- Phase 4: restore snapshot to a fresh stack on the old key (old per-env KMS resources still live in security.template until Phase 5).
- Phase 5 is the point of no return for the KMS migration.

## Resolved Decisions

The original draft of this change carried five open questions. They were resolved as follows:

- **Q1 — Separate prod-deployer GitHub Actions credentials?** **YES, in this change.** Added `GitHubActionsUserProd` IAM user in `bootstrap-prod` and branch-conditional credential selection in `zbuild.yml` (see D4). Increases scope by ~one workflow step, one IAM user, one IAM policy, and two new GitHub secrets, but gives clean prod/nonprod CI separation now.
- **Q2 — `dev` data loss tolerance?** **OK to drop and recreate empty.** `dev` is a test env (per CLAUDE.md); no snapshot/restore needed. Saves several steps in Phase 3.
- **Q3 — Single change or two?** **Split.** The us-west-2 → us-east-2 migration is the sibling change [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md). That change depends on this one landing first (it builds on the `bootstrap-prod`/`bootstrap-nonprod` pattern).
- **Q4 — Existing orphaned keys.** **Yes, sweep in Phase 5.** Inventory in Phase 0 task 1.2; schedule deletion in Phase 5 task 7.1.
- **Q5 — First `bootstrap-prod` deploy.** **Existing human admin IAM user.** Identify which user in Phase 0 task 1.1; that user runs the manual `aws cloudformation deploy` for `bootstrap-prod` in both regions during Phase 1. No chicken-and-egg: the admin user already has AdministratorAccess (or equivalent) independent of anything `bootstrap-prod` creates.
- **Q6 — Backwards compatibility with the existing `bootstrap` stack?** **No.** A first attempt added `Condition: IsUnscoped` to every existing resource so the live stack could be updated in place; a `--no-execute-changeset` validation surfaced two unrelated pre-existing drift items (`GoogleOAuthSecrets` removal, `TemplatesBucketPolicy` replacement) that complicated the deploy story. The user opted to accept full teardown of the legacy `bootstrap` stack and split the content into a clean `bootstrap-shared` + `bootstrap-prod` + `bootstrap-nonprod` trio (see D1). Trade-offs (ECR rebuild, non-prod secret re-issue, brief deploy quiet window) are accepted.
