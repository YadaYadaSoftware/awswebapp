## Context

The deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) creates one CloudFormation stack per branch, named `{branch-leaf}-{processed-domain}`. For shared branches (`app`, `beta`, `alpha`, `dev`) the stack uses `master.template` (full backend incl. Aurora Global Cluster). For every other branch it uses `application.template` and imports backend exports from the `dev` stack.

Today there is no automated teardown path. When a feature branch is deleted on GitHub, the stack lingers and continues to bill for ECS tasks, ALBs, NAT egress on the imported VPC, and Route53 records. The deploy job also stages a per-branch packaged template at `s3://cf-templates-{account}-{region}/{branch-leaf}/application-template.yaml`; those S3 objects are likewise orphaned.

GitHub fires a `delete` workflow event when a branch (or tag) is removed via the UI, API, or `git push --delete`. The event payload includes `ref_type` (`branch` or `tag`) and `ref` (the deleted branch name, full path including any `feature/` prefix). This is the natural trigger for cleanup.

The deploy job itself ran only in `us-east-1` for non-shared branches (the matrix sets `deploy: true` for `us-east-1` and conditional-on-shared-branch for `us-west-2`). So cleanup for non-shared branches only needs to target `us-east-1`.

## Goals / Non-Goals

**Goals:**
- Automatically delete the per-branch CloudFormation stack when its branch is deleted from the remote.
- Refuse to act on protected branches (`app`, `beta`, `alpha`, `dev`) under any circumstance — these are shared infrastructure and their lifecycle is managed by other means.
- Provide clear, self-documenting workflow output (stack name, region, final status) so the run is auditable.
- Clean up the corresponding S3 prefix in the CF templates bucket so packaged templates don't accumulate.
- Fail loudly (red workflow run) if deletion does not reach `DELETE_COMPLETE`, so the operator notices and can intervene.

**Non-Goals:**
- Cleaning up ECR images tagged with the branch name. The ECR repo is shared across branches and images are content-addressed by source checksum; tag pruning is a separate concern.
- Reconciling already-orphaned stacks from before this workflow was introduced. A one-shot manual sweep covers those.
- Deleting Route53 hosted zones, SES identities, or any other globally-shared resource — the stack only owns A/AAAA records inside the existing hosted zone, and CloudFormation deletes those as part of stack deletion.
- Tag deletion. The `delete` event also fires for tags; this workflow filters to `ref_type == 'branch'` only.
- Cross-region cleanup for branches that somehow deployed multi-region. By policy, only the four protected branches do that, and those are skipped entirely.

## Decisions

### Decision 1: Trigger on the `delete` event, not `pull_request: closed`

**Choice:** Use `on: delete` with a filter for `ref_type == 'branch'`.

**Alternatives considered:**
- `on: pull_request` with `types: [closed]` — only fires for PR-driven branch lifecycles. Branches deleted directly (`git push origin --delete`, manual UI delete without a PR, or merged via squash where the GitHub "delete branch" button is clicked later) would be missed. Some PRs are closed without merging and the branch is kept.
- `on: schedule` reconciliation — could enumerate stacks and cross-check against `git branch -r`. Heavier, requires listing every stack each run, and has a window of waste before the next cron fire.

**Rationale:** The `delete` event fires exactly once at the moment of branch deletion regardless of which UI/tool triggered it. It is also the cheapest possible trigger (no scheduled cost, no per-PR coupling).

### Decision 2: Hard-coded protected-branch deny-list inside the workflow

**Choice:** The workflow contains an early `if` step that exits successfully (no-op) when the deleted branch is `app`, `beta`, `alpha`, or `dev`.

**Alternatives considered:**
- Branch protection rules on GitHub. These prevent deletion in the first place but rely on repo admins configuring them correctly, and don't help if someone bypasses with admin privileges.
- A separate workflow file per protected branch. Excessive duplication.

**Rationale:** Defense in depth. Even if branch protection is removed or bypassed, the cleanup workflow itself must refuse to tear down shared infrastructure. The list is duplicated in two places (here and the deploy job's `is_primary`/template-selection logic), but it is a tiny, stable list and clarity outweighs DRY. The list lives at the top of the workflow as a single bash variable to make audits easy.

### Decision 3: Compute the stack name with the same formula as the deploy job

**Choice:** `STACK_NAME="${BRANCH_LEAF}-${PROCESSED_DOMAIN}"` where `BRANCH_LEAF=${REF##*/}` and `PROCESSED_DOMAIN=$(echo "$DOMAIN_NAME" | tr '.' '-')`.

**Alternatives considered:**
- Tag the stack with its branch name at deploy time and discover it by `aws cloudformation describe-stacks --query Stacks[?Tags[?...]]`. More resilient if the naming convention ever changes, but adds a new tag dependency to `infrastructure/application.template` and adds an extra AWS call.

**Rationale:** The naming convention is stable and already enforced by the deploy job. Recomputing it is two lines of bash with no extra API calls. If the convention changes, both places update in lockstep; this is acceptable because they are owned by the same workflow author.

### Decision 4: `us-east-1` only

**Choice:** Hard-code `AWS_REGION=us-east-1` in the cleanup job.

**Rationale:** Non-protected branches only ever deploy a single-region stack in `us-east-1` (see the deploy matrix — `deploy: true` for `us-east-1`, conditional-on-shared-branch for `us-west-2`). Protected branches are filtered out earlier. Therefore there is never a `us-west-2` stack to delete for a branch the cleanup workflow would touch.

### Decision 5: Wait for `DELETE_COMPLETE` synchronously, with a 30-minute timeout

**Choice:** Call `aws cloudformation delete-stack` then `aws cloudformation wait stack-delete-complete` (the AWS CLI built-in waiter, which polls every 30s for up to 30 minutes).

**Alternatives considered:**
- Fire-and-forget. Cheaper run minutes but the operator never sees `DELETE_FAILED` until they happen to look in CloudFormation. Defeats the "fail loudly" goal.
- A custom polling loop with a longer timeout. The built-in waiter is good enough; if 30 minutes isn't enough the stack is stuck on something (ENI orphaned by Fargate, retained resource) that requires human action anyway.

**Rationale:** Stack deletion is usually < 5 minutes for `application.template` (one ECS service draining, ALB deletion, TGs, SGs, Route53 record). 30 minutes is generous headroom. A timeout failing the workflow is the desired signal.

### Decision 6: S3 prefix cleanup runs *after* the stack is gone

**Choice:** Delete `s3://cf-templates-{account}-us-east-1/{branch-leaf}/` only after `wait stack-delete-complete` succeeds.

**Rationale:** If CloudFormation needs to re-read the original template during deletion rollback, removing it first would block recovery. Running cleanup post-deletion is safer.

### Decision 7: Use existing CI IAM principal — do not provision new credentials

**Choice:** Reuse `secrets.AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` configured via `aws-actions/configure-aws-credentials@v4`.

**Rationale:** Avoids the operational overhead of a second key. The principal already has the broad permissions needed to deploy (which include `cloudformation:CreateStack`/`UpdateStack`); `DeleteStack` on the same stack name pattern is a natural extension. If audit policy requires a narrower role for deletion specifically, that can be a follow-up — the current scope is enabling the workflow, not re-scoping IAM.

## Risks / Trade-offs

- **[Risk]** A branch matching a protected name suffix (e.g. `feature/dev`) would have a branch-leaf of `dev`, which the deny-list happens to also block, causing cleanup to no-op for that branch. **Mitigation:** This is a pre-existing footgun in the deploy naming scheme — `feature/dev` would have collided with the real `dev` stack name on deploy too. The cleanup behavior (refuse to delete) is the conservative outcome. Document it in tasks.md verification.
- **[Risk]** `delete-stack` succeeds but a resource is stuck in `DELETE_FAILED` (e.g. an S3 bucket with objects, a security group still referenced). Waiter reports failure and the workflow fails red. **Mitigation:** This is the desired outcome — surface the failure, let a human resolve. The workflow run links the CloudFormation events.
- **[Risk]** The deploy job ran but the stack was never created (e.g. template validation failed before stack creation). `delete-stack` on a nonexistent stack returns success but `describe-stacks` first will 404. **Mitigation:** Pre-check with `aws cloudformation describe-stacks --stack-name $STACK_NAME` and exit 0 with a friendly message if absent. The workflow run is still recorded.
- **[Risk]** Someone renames a branch (delete + create with new name) while a deploy is in flight. The cleanup workflow would race the deploy. **Mitigation:** Same `concurrency:` group as the deploy job (`deploy-us-east-1-{branch-leaf}`), with `cancel-in-progress: false`. Cleanup serializes behind any active deploy.
- **[Trade-off]** Cleanup does not delete ECR images tagged with the branch name. **Mitigation:** Out of scope by Decision; documented in proposal Impact. If image bloat becomes a problem, add an ECR lifecycle policy or a separate cleanup step.
- **[Trade-off]** The protected-branch list is duplicated between deploy and cleanup workflows. **Mitigation:** Accepted — single bash variable in each file, easy to audit. Extracting to a shared composite action is over-engineering for a 4-element list.
- **[Gotcha]** GitHub Actions only dispatches the `delete` event for workflow files that exist **on the default branch** at the moment the event fires. A new `cleanup-on-branch-delete.yml` sitting on a feature branch will silently do nothing for any branch deletion until it has been merged into `app`. **Mitigation:** Call this out in the Migration Plan and in `BRANCH_MANAGEMENT_README.md`. The same constraint applies to `create`, `fork`, `schedule`, and several other "repository-level" events — it is a GitHub Actions design choice, not something we can work around in the YAML.

## Migration Plan

1. **Merge this workflow file into `app` (the default branch).** *Without this step the workflow will never fire — the `delete` event is dispatched only from the default branch's copy of the workflow file.* Going through `dev` first is fine (so the existing CI runs validate it), but the workflow does not become live until it lands on `app`.
2. No backfill needed — workflow only acts on future branch deletions.
3. Manually sweep existing orphaned stacks (out of scope for this change; track separately if any exist).
4. **Rollback:** Delete the workflow file from `app`. No infrastructure state to revert.

## Open Questions

- None blocking. IAM verification (does the existing CI principal already have `cloudformation:DeleteStack`?) is a tasks.md item — if it does not, an inline policy update or attached managed-policy expansion will be needed before the workflow is functional. The workflow's first real run will surface this immediately as an AccessDenied error, which is acceptable for the first iteration.
