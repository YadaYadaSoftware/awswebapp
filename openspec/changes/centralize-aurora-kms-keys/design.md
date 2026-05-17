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

### D1. One template, one stack per region

A single rewritten [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) owns everything that lived in the legacy `bootstrap` stack **plus** the new KMS + IAM resources for both scopes:

- **Shared infrastructure (always created in every region):** `WebECRRepository` (with explicit `RepositoryName: !Sub "ecr-${AWS::AccountId}-${AWS::Region}"` to match the workflow's hardcoded image URI), `ApiGatewayCloudWatchLogsRole` + `ApiGatewayAccount`, `LogRetentionConfigFunction` + `ConfigRuleRole` + `LogRetentionConfigRule` + `LogRetentionRemediationDocument` + `SSMAutomationRole` + `LogRetentionRemediation` + `ConfigInvokeLambdaPermission`, `TemplatesBucketPolicy` on the externally-managed `cf-templates-{account}-{region}` bucket.
- **Both KMS keys, always (one primary in us-east-1 + one replica in us-west-2 each):** `AuroraKmsKeyNonprod`/`AuroraKmsKeyNonprodReplica`, `AuroraKmsKeyProd`/`AuroraKmsKeyProdReplica`. Aliases `alias/taskmanager-aurora-nonprod` and `alias/taskmanager-aurora-prod`. SSM parameters `/taskmanager/kms/nonprod/aurora-key-arn` and `/taskmanager/kms/prod/aurora-key-arn`.
- **Both GitHub Actions IAM users (primary region only — IAM is global):** `GitHubActionsUser` + `AccessKey` + `DeploymentPolicy` (nonprod CI), `GitHubActionsUserProd` + `AccessKey` + `DeploymentPolicy-prod` (prod CI), `ProdKmsAdminRole` (human-assumable role with `kms:*` on the prod key).

**Parameters (5 total):**
- `TemplatesBucketName` (required) — the externally-created S3 bucket the templates-bucket-policy attaches to.
- `RetentionDays` (Default 7) — CloudWatch log retention enforced by the Config rule.
- `PrimaryNonprodKeyArn` (Default `""`) — set in the replica region; empty (default) means this is the primary region.
- `PrimaryProdKeyArn` (Default `""`) — same shape, paired with above.
- `KeyAdminPrincipalArn` (Default `""`) — optional extra principal added to the prod key's `NotPrincipal` Deny exemption. Required during initial setup because KMS's lockout-safety check rejects policy updates that would prevent the calling principal from updating the policy. Once `prod-kms-admin` is the standard admin path, set back to empty.

**Conditions (3 total):**
- `IsPrimary` — `!Equals [!Ref PrimaryNonprodKeyArn, ""]`. The primary region creates the multi-region KMS keys directly + the IAM users.
- `IsReplica` — the inverse. Replica region creates `AWS::KMS::ReplicaKey` instead.
- `HasKeyAdmin` — drives the conditional `!If` that extends the prod key's NotPrincipal list.

Net result per region: **one** stack instance, named `bootstrap`. The legacy `bootstrap` stack content is fully retired.

**Why both scopes coexist in one stack** (vs. the earlier three-stack draft of `bootstrap-shared` + `bootstrap-prod` + `bootstrap-nonprod`):

- The security boundary between prod and nonprod is enforced by the **prod key's `NotPrincipal+Deny`** (see D3), not by stack-level isolation. The Deny names exactly the four allowed principals and blocks everyone else — including the nonprod CI user — from the prod key regardless of what other IAM permissions they hold. Splitting into separate stacks added nothing to that boundary.
- Per-region AWS singletons (`AWS::ApiGateway::Account`, `AWS::S3::BucketPolicy` on the templates bucket) **cannot** exist in multiple stacks — they have to live in exactly one. The earlier "three stacks per region" design needed a dedicated `bootstrap-shared` just to own them. Collapsing to one stack removes that dance.
- Operational complexity drops a lot. One stack → one deploy command per region, no "deploy quiet window" coordination between three stacks, no risk of partial deployment leaving things half-wired. The teardown from the legacy `bootstrap` becomes a straightforward delete + redeploy rather than a multi-stage cutover.
- The trade-off: deleting `bootstrap` to "blow away just prod" is no longer possible — it'd take nonprod down with it. That's acceptable because deleting `bootstrap` is a rare emergency operation, not a routine one.

**Alternatives considered (and rejected):**
- **Three stacks per region** (`bootstrap-shared` + `bootstrap-prod` + `bootstrap-nonprod`). Earlier draft. Rejected: more operational complexity for a security boundary the key policy already enforces. See "Why both scopes coexist in one stack" above.
- **Update legacy `bootstrap` in place** rather than tear-down + redeploy. Tried earlier — a `--no-execute-changeset` against the live stack surfaced unrelated pre-existing drift (`GoogleOAuthSecrets` removal, `TemplatesBucketPolicy` replacement) that complicated the deploy story.
- **Put `WebECRRepository` in `bootstrap-prod` and `bootstrap-nonprod` (two separate ECR repos for image isolation).** Rejected as a non-goal of this change — the existing CI image-tag content-addressing assumes one ECR per region. Splitting ECR is left as a possible follow-up.

**Note on the `GitHubActionsUser` re-issue:** the legacy `bootstrap` stack's CI user is destroyed when the stack is deleted, and the new bootstrap's `GitHubActionsUser` is a fresh IAM user with a fresh access key. The existing `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` GitHub secrets must be updated from the new stack output before any non-prod CI deploy will succeed. Treat this rotation as a hard part of the cut-over, not an afterthought.

### D2. KMS key discovery via SSM Parameter Store, not CloudFormation Exports

The consolidated `bootstrap` stack writes both key ARNs to known SSM parameters:

```
/taskmanager/kms/prod/aurora-key-arn      (in each region)
/taskmanager/kms/nonprod/aurora-key-arn   (in each region)
```

Env stacks discover the appropriate key by parameter lookup at deploy time (workflow `aws ssm get-parameter` call, then passed as a CloudFormation parameter).

**Why not CloudFormation Exports:** Exports create a hard cross-stack dependency. Once an env stack imports an export, the bootstrap stack cannot be updated in a way that touches the export until every consumer is removed. With dozens of feature branches plus `dev`/`alpha`/`beta`/`app` all consuming, the bootstrap stack would become un-updatable.

**Why not a workflow `describe-stacks` lookup:** Also workable, but it means every consumer must hard-code the bootstrap stack name. SSM gives us one canonical, region-aware lookup.

### D3. Prod KMS key policy: split read/use vs destroy

The prod key policy allows three sets of principals different sets of actions:

| Principal                          | Allowed actions                                                                                                                   |
|------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------|
| `rds.amazonaws.com` (service)      | `CreateGrant`, `DescribeKey`, `Decrypt`, `Encrypt`, `GenerateDataKey`, `ReEncryptFrom`, `ReEncryptTo`, `RetireGrant`              |
| `GitHubActionsUserProd` IAM user   | Same set as RDS (so it can wire up grants when creating Aurora clusters), plus `ListGrants`, `RevokeGrant` for cleanup            |
| Account root + `prod-kms-admin` role | Everything (`kms:*`)                                                                                                            |

Notably, `GitHubActionsUserProd` does **not** get `ScheduleKeyDeletion`, `DisableKey`, `PutKeyPolicy`, `DeleteAlias`, `UpdateAlias`, `ReplicateKey` on the prod key. Even if the prod CI credentials leak, the prod key cannot be destroyed by them.

**`AllowAccountRoot` is paired with a `NotPrincipal` default-deny.** Without this, the standard `AllowAccountRoot kms:* Resource:*` statement delegates evaluation to IAM, and any IAM user in the account whose own IAM policy grants `kms:*` (e.g. the broad legacy `DeploymentPolicy` that `GitHubActionsUser` still carries) can reach the prod key — including decrypting prod data. To close that gap, the prod key policy includes a final `Effect: Deny` statement with `NotPrincipal` listing only the authorized four (root, `GitHubActionsUserProd`, `prod-kms-admin`, `rds.amazonaws.com`). Anything not in that list is explicitly denied, overriding the IAM-delegation pathway. (Discovered during T3.C of the access-control validation — nonprod CI was able to `DescribeKey` on the prod key until the Deny was added.)

**`KeyAdminPrincipalArn` parameter exists to handle KMS lockout protection during setup.** When AWS KMS applies a key policy update, it runs a lockout-safety check: the calling principal must still be able to call `kms:PutKeyPolicy` under the new policy. Our Deny statement covers `kms:*`, so the deployer (typically an IAM user like `developer-tim`) is denied along with everyone else not in the NotPrincipal exemption — and the deploy fails with *"The new key policy will not allow you to update the key policy in the future."* The `KeyAdminPrincipalArn` parameter (optional, default empty) adds one more principal to the NotPrincipal list, intended for the deploying user during initial setup. The long-term path is for the deployer to assume `prod-kms-admin` (which is already in the exemption) and run deploys from that role, at which point this parameter can be left empty. (Discovered when applying the NotPrincipal Deny in the first place — the update was rejected by KMS's lockout check.)

`prod-kms-admin` is created in the same `bootstrap` stack with `AssumeRolePolicyDocument` allowing only `AWS::AccountId:root` — so a human operator with console/SSO access can assume it; no CI user can.

The nonprod key policy is the existing broad policy (CI can do everything) — nonprod must stay friction-free. No `NotPrincipal+Deny` clause on the nonprod key.

### D4. Separate `GitHubActionsUserProd` IAM user, branch-conditional credentials in zbuild.yml

The consolidated `bootstrap` stack (primary region only) creates `GitHubActionsUserProd` — a new IAM user with its own access key pair and its own `DeploymentPolicy-prod` policy that mirrors the shape of the existing `DeploymentPolicy` but is scoped to the resources needed for the `app` env stack. The nonprod CI user `GitHubActionsUser` is created in the same stack.

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
- **[Risk] `bootstrap` deployment by a non-privileged user accidentally widens the prod key policy.** → Mitigate: the prod key's `NotPrincipal+Deny` only allows updates from root, `prod-kms-admin`, `GitHubActionsUserProd`, and (during setup) the `KeyAdminPrincipalArn` exemption. Even with `kms:*` IAM permissions, an unauthorized principal cannot rewrite the policy.
- **[Risk] `GitHubActionsUserProd` access key leaks before key rotation policy is in place.** → Mitigate: same deploy/rotation policy as the existing CI user; access key is created in CloudFormation but the operator copies it once from the stack output into GitHub secrets. Tasks.md mandates rotating the key after the initial setup.
- **[Trade-off] One bootstrap stack per region, not three.** Earlier draft had three (`bootstrap-shared` + `bootstrap-prod` + `bootstrap-nonprod`). Collapsed to one because (a) the security boundary is enforced by the prod key's `NotPrincipal+Deny`, not stack-level isolation, and (b) per-region AWS singletons (`ApiGatewayAccount`, the S3 bucket policy on the templates bucket) couldn't exist in multiple stacks anyway. Cost: deleting `bootstrap` to "blow away just prod" is no longer possible — but that's a rare emergency operation, not a routine one.
- **[Trade-off] Branch-conditional credentials add one moving part to the workflow.** If the `app` branch's prod secrets are missing or invalid, the deploy fails fast at the `configure-aws-credentials` step rather than silently falling back to non-prod credentials. The workflow explicitly errors if `AWS_ACCESS_KEY_ID_PROD` is empty on an `app` push.
- **[Risk] Teardown of the existing `bootstrap` stack briefly removes `TemplatesBucketPolicy` and `ApiGatewayAccount`.** During that gap, any CI deploy attempting to upload a packaged template to the S3 bucket will fail. → Mitigate: schedule the teardown for a deploy quiet window (no in-flight PRs, no pending merges), and stand the new `bootstrap` up immediately after the delete completes.
- **[Risk] Reissuing both non-prod and prod CI access keys at the same time risks an "all CI broken" window if the operator forgets to update GitHub secrets.** → Mitigate: the tasks list both secret updates as required gates before the next CI run; the consolidated bootstrap exposes both access keys as outputs the operator can copy once.
- **[Trade-off] Loss of one ECR image cache.** When `WebECRRepository` is recreated by the new bootstrap, all existing image tags are gone. The next CI deploy on any branch triggers a full Docker rebuild (~3 minutes per region). Subsequent deploys benefit from the new ECR's empty-then-populated cache normally.

## Migration Plan

**Phase 0 — Prereqs (manual, ~30 min)**
1. Identify which existing human admin IAM user will deploy the new `bootstrap` for the first time (Resolved Decisions Q5).
2. Inventory existing orphaned `AuroraKmsKey` resources in `us-east-1` and `us-west-2` for the Phase 5 sweep.
3. Also inventory any in-flight `bootstrap-prod` / `bootstrap-nonprod` stacks from earlier iteration drafts — they'll be torn down in Phase 1.

**Phase 1 — Bootstrap teardown and stand-up (deploy quiet window required)**

This phase replaces the legacy `bootstrap` stack (and any leftover `bootstrap-prod`/`bootstrap-nonprod` stacks from earlier iteration) with a single consolidated `bootstrap` stack per region. Schedule a deploy quiet window — no in-flight PRs, no pending merges — because the `TemplatesBucketPolicy` and `ApiGatewayAccount` are briefly absent between the teardown and the new `bootstrap` coming up.

3. Merge template changes to the working branch: the rewritten `bootstrap.template`, updated `security.template` (KMS resources still present for now — Phase 5 removes them).
4. Quiet window opens. Human admin deletes any leftover scoped bootstraps from earlier iteration: `aws cloudformation delete-stack --stack-name bootstrap-prod --region us-east-1` and `--region us-west-2`; same for `bootstrap-nonprod`. Wait for all four to reach `DELETE_COMPLETE`.
5. Human admin deletes the legacy `bootstrap` stack in both regions. Wait for `DELETE_COMPLETE`.
6. Human admin deploys the new consolidated `bootstrap` in `us-east-1` (primary): `aws cloudformation deploy --stack-name bootstrap --template-file infrastructure/bootstrap.template --parameter-overrides TemplatesBucketName=cf-templates-<account>-us-east-1 KeyAdminPrincipalArn=<deployer-iam-user-arn> --capabilities CAPABILITY_NAMED_IAM --region us-east-1`. Captures the new `GitHubActionsUser` and `GitHubActionsUserProd` access keys + secrets from stack outputs.
7. Human admin deploys the new consolidated `bootstrap` in `us-west-2` (replica): pass `TemplatesBucketName=cf-templates-<account>-us-west-2 PrimaryNonprodKeyArn=<from us-east-1> PrimaryProdKeyArn=<from us-east-1> KeyAdminPrincipalArn=<deployer-iam-user-arn>`. The replica deploy doesn't create IAM users (they're global), only KMS replica keys + aliases + SSM parameters + the shared infra resources for `us-west-2`.
8. Update GitHub repository secrets: rotate `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` to the new `GitHubActionsUser` values, add new `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` from `GitHubActionsUserProd`.
9. Verify all four SSM parameters exist and resolve in both regions (`/taskmanager/kms/{prod,nonprod}/aurora-key-arn`).
10. Quiet window closes — normal CI resumes.

**Phase 2 — Wire workflows and templates**
11. Merge workflow + template changes (branch-conditional credentials, SSM lookup, backend.template rewiring, master.template KmsKeyArn required, security.template export removal).
12. Push to a throwaway feature branch and verify the deploy succeeds using the new non-prod CI user. (Feature branches don't exercise the KMS path itself but they prove the workflow plumbing works.)

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
- Phase 1: the legacy `bootstrap` stack is gone after step 5 — there is no clean revert to the old layout. Recovery means re-deploying the old `bootstrap.template` content from git history as a new stack and rotating GitHub secrets back. Mechanical but slow (~30 min). Plan the quiet window for a time when rolling forward is the only realistic option.
- Phase 2: revert the workflow / template merge; the old `security.template` still owns the per-env KMS keys; existing env stacks continue using them. (The new bootstrap's keys are unused but harmless.)
- Phase 3: restore snapshot to a fresh stack on the old key.
- Phase 4: restore snapshot to a fresh stack on the old key (old per-env KMS resources still live in security.template until Phase 5).
- Phase 5 is the point of no return for the KMS migration.

## Resolved Decisions

The original draft of this change carried five open questions. They were resolved as follows:

- **Q1 — Separate prod-deployer GitHub Actions credentials?** **YES, in this change.** Added `GitHubActionsUserProd` IAM user in `bootstrap-prod` and branch-conditional credential selection in `zbuild.yml` (see D4). Increases scope by ~one workflow step, one IAM user, one IAM policy, and two new GitHub secrets, but gives clean prod/nonprod CI separation now.
- **Q2 — `dev` data loss tolerance?** **OK to drop and recreate empty.** `dev` is a test env (per CLAUDE.md); no snapshot/restore needed. Saves several steps in Phase 3.
- **Q3 — Single change or two?** **Split.** The us-west-2 → us-east-2 migration is the sibling change [`shift-secondary-region-to-us-east-2`](../shift-secondary-region-to-us-east-2/proposal.md). That change depends on this one landing first (it builds on the `bootstrap-prod`/`bootstrap-nonprod` pattern).
- **Q4 — Existing orphaned keys.** **Yes, sweep in Phase 5.** Inventory in Phase 0 task 1.2; schedule deletion in Phase 5 task 7.1.
- **Q5 — First bootstrap deploy.** **Existing human admin IAM user.** Identify which user in Phase 0 task 1.1; that user runs the manual `aws cloudformation deploy` for the new `bootstrap` in both regions during Phase 1, passing their own ARN as `KeyAdminPrincipalArn` to satisfy the prod key's lockout-safety check. No chicken-and-egg: the admin user already has AdministratorAccess (or equivalent) independent of anything the new `bootstrap` creates.
- **Q6 — Backwards compatibility with the existing `bootstrap` stack?** **No.** A first attempt added `Condition: IsUnscoped` to every existing resource so the live stack could be updated in place; a `--no-execute-changeset` validation surfaced unrelated pre-existing drift items (`GoogleOAuthSecrets` removal, `TemplatesBucketPolicy` replacement) that complicated the deploy story. The user opted to accept full teardown of the legacy `bootstrap` stack.
- **Q7 — Three stacks per region or one?** **One.** The initial teardown plan deployed three stacks per region (`bootstrap-shared` + `bootstrap-prod` + `bootstrap-nonprod`). After exercising the design end-to-end with deploys + access-control tests, we collapsed to a single consolidated `bootstrap` stack per region. The security boundary between prod and nonprod is enforced by the prod key's `NotPrincipal+Deny` (see D3), not by stack isolation, and per-region AWS singletons (`ApiGatewayAccount`, the templates bucket policy) couldn't be cleanly split across stacks anyway. Trade-off: deleting `bootstrap` to "blow away just prod" is no longer possible — but that operation isn't routine, so the loss is acceptable in exchange for a much simpler deploy story (one stack per region, no quiet-window coordination across three).
- **Q8 — Move `SharedLambdaExecutionRole` to bootstrap and delete `security.template` entirely?** **Deferred.** After the Section 4 cleanup, `security.template` contains only `SharedLambdaExecutionRole` and its `SharedLambdaRoleArn-${BranchName}` export. Conceptually the role belongs in bootstrap (IAM is global; per-env duplication has identical policies and zero cost benefit). But the export is `Fn::ImportValue`'d by [api.template](../../../infrastructure/api.template) and [web.template](../../../infrastructure/web.template) using the `-${EnvironmentToImport}` suffix pattern; moving the role to bootstrap requires threading a new `SharedLambdaRoleArn` parameter through the whole chain (workflow SSM lookup → master → application → api/web) and rewriting those `Fn::ImportValue`s to `!Ref`. That's a 7-file refactor and right now we're trying to push dev to validate this KMS change end-to-end — adding more in-flight refactor risk would conflate failure causes. Tracked as a follow-up in [tasks.md](tasks.md#9-follow-ups-after-this-change-archives). Spec/proposal unchanged because the deferred work is a future change, not a property of this one.
