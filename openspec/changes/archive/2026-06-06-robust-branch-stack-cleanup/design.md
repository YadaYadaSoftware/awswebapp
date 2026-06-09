## Context

The cleanup workflow at [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml) runs on the GitHub `delete` event (branch deletions). Today it:

1. Validates the deleted ref is a branch (not a tag).
2. Computes `{branch-leaf}-{processed-domain}` as the stack name.
3. Skips if the leaf is `app` / `beta` / `alpha` / `dev` (protected branches).
4. Calls `aws cloudformation delete-stack --stack-name <name> --region us-east-1`.
5. Waits with `aws cloudformation wait stack-delete-complete` (30-minute timeout).
6. After `DELETE_COMPLETE`, deletes the per-branch prefix from the templates S3 bucket.

The failure mode this change addresses: between steps 4 and 6, CFN attempts to delete each resource in the stack tree and fails on the first one that holds state. Common offenders, observed in this project:

- S3 buckets created by env stacks (none exist today — bootstrap owns the only bucket — but the env-stack pattern could add one in the future, and other consumers of these templates will).
- ECR repositories created by env stacks (same shape — bootstrap owns the live one today, but future repos in env stacks are likely).
- Aurora clusters in `backing-up` state. `DeletionProtection: false` is already set on env-stack clusters, but `backing-up` is an in-flight automated backup that takes precedence — CFN sees a stuck state, not a permission error.

The sibling proposal [`robust-aurora-cluster-teardown`](../robust-aurora-cluster-teardown/proposal.md) defines the Aurora-specific force-teardown mechanism (a Lambda-backed CFN custom resource that calls `delete-db-cluster --skip-final-snapshot` past the `backing-up` state). This change orchestrates around it but does not duplicate it.

## Goals / Non-Goals

**Goals:**
- Cleanup workflow handles the three known classes of blocker preemptively: bucket-with-objects, repo-with-images, cluster-in-backing-up.
- Workflow operates only on resources that are CFN-tracked members of the stack being deleted. Never touches resources from other stacks.
- Workflow enumerates the full stack tree (recursing into nested `AWS::CloudFormation::Stack` resources) before any destruction.
- A workflow summary lists every resource touched, even on success — so the operator can audit what got cleaned.
- Failures are loud: if pre-cleanup of any resource fails, the workflow stops before `delete-stack`. If `delete-stack` fails after pre-cleanup, the failure is genuinely surprising and worth surfacing.

**Non-Goals:**
- Generic blocker discovery. We handle three specific resource types. Adding more (Lambda layers, ENIs, NAT gateways with EIPs) is future work.
- Retry-on-DELETE_FAILED. One pre-cleanup pass, one `delete-stack` attempt.
- Cross-region cleanup. The existing single-region constraint stands (`us-east-1` only).
- Cleaning up resources the stack DOESN'T own. We do not search for "orphan resources matching the branch name" — only what `describe-stack-resources` reports.
- Bootstrap-stack teardown. The existing protected-branch list (`app`/`beta`/`alpha`/`dev`) covers the shared infrastructure branches; their stacks would be torn down by hand, not by this workflow.

## Decisions

### D1. Use `list-stack-resources` (not `describe-stack-resources`)

`list-stack-resources` is paginated and returns up to 100 resources per page; suitable for stacks with hundreds of resources. `describe-stack-resources` returns up to 100 with no pagination — fine for small stacks but unsafe as a general primitive.

**Recursion**: for each resource of type `AWS::CloudFormation::Stack`, the workflow recurses by calling `list-stack-resources` on the `PhysicalResourceId` (which is the nested stack's ARN/name).

### D2. Empty buckets via `s3api delete-objects` with paginated `list-object-versions`

S3 buckets with versioning enabled (like the bootstrap's `TemplatesBucket` — though that's not in env stacks today) need every version + every delete marker deleted before the bucket itself can go. The pattern:

```bash
aws s3api list-object-versions --bucket "$bucket" --output json |
  jq -c '{Objects: ([.Versions, .DeleteMarkers] | flatten | .[] | {Key, VersionId})}' |
  while read -r batch; do
    aws s3api delete-objects --bucket "$bucket" --delete "$batch"
  done
```

(Details to be tightened in the implementation — this is the conceptual shape.)

`aws s3 rm s3://bucket --recursive` would work for unversioned buckets but skips versions/delete-markers, so `s3api` is safer.

### D3. Delete ECR images via `batch-delete-image` with paginated `list-images`

```bash
aws ecr list-images --repository-name "$repo" --query 'imageIds[*]' --output json |
  # batch into chunks of <= 100 (batch-delete-image's max)
  jq -c '.[0:100]' |
  while read -r batch; do
    aws ecr batch-delete-image --repository-name "$repo" --image-ids "$batch"
  done
```

The repo itself is deleted by `delete-stack` afterward, not by this step.

### D4. Aurora teardown via the mechanism from `robust-aurora-cluster-teardown`

This change does not define the Aurora teardown. It calls into whatever mechanism `robust-aurora-cluster-teardown` defines. Two possible integration shapes:

- **Inline CLI** (simpler): the workflow calls `aws rds delete-db-cluster --skip-final-snapshot` directly for each `AWS::RDS::DBCluster` in the stack tree, then waits. This bypasses CFN's removal of the cluster, leaving CFN to "find" the cluster already gone when it tries to delete it.
- **CFN custom resource** (cleaner but heavier): the env-stack template adds a custom resource that, on stack delete, invokes the force-teardown Lambda. `robust-aurora-cluster-teardown` would land that custom resource.

This change is agnostic — when `robust-aurora-cluster-teardown` lands, this workflow integrates with whatever it provides. If `robust-aurora-cluster-teardown` lands AFTER this one, the workflow uses the inline-CLI shape as an interim implementation.

### D5. Safety: never touch what isn't in the stack tree

Every destructive operation is gated on the resource appearing in `list-stack-resources` for the stack tree being deleted. The workflow does NOT:

- Search S3 for buckets named "matching" the branch.
- Search ECR for repositories matching the branch.
- Match by tag, by name pattern, or by anything other than CFN ownership.

Misnamed or hand-modified resources will not be touched. Resources owned by OTHER stacks (notably the bootstrap stack's `TemplatesBucket`, `WebECRRepository`, KMS keys) will not be touched even if they appear in the same account+region.

### D6. Failure mode: stop before `delete-stack` if pre-cleanup fails

If any of {enumerate, empty-bucket, delete-images, kill-cluster} fails for any resource, the workflow exits non-zero BEFORE calling `delete-stack`. The half-deleted state is preserved for operator inspection rather than amplified by trying to delete the parent stack on top.

The workflow summary still gets written so the operator can see which resource failed.

### D7. Workflow summary: complete audit trail

Every resource visited gets logged to `$GITHUB_STEP_SUMMARY`:

```
## Resources enumerated
- Stack: feature-foo-appcloud-systems (top-level)
  - AWS::S3::Bucket: feature-foo-attachments → 47 objects, 0 versions, 0 markers → EMPTIED
  - AWS::ECR::Repository: feature-foo-images → 3 images → DELETED
  - AWS::CloudFormation::Stack: BackendStack → arn:...
    - AWS::RDS::DBCluster: feature-foo-cluster → backing-up → FORCE-DELETED
```

This is the audit trail for what got destroyed and serves as forensic record if anyone later asks "where did that bucket go".

## Risks / Trade-offs

- **[Risk] Workflow runs longer.** Pre-cleanup adds AWS API calls — small stacks add <30s, large stacks (many buckets with many versions) could add minutes. → Mitigation: the existing 30-minute timeout is plenty for any reasonable stack; if a single bucket has so many objects that emptying it exceeds the timeout, that's a signal the bucket shouldn't be in an env stack to begin with.

- **[Risk] Pre-cleanup masks legitimate "don't delete me yet" intent.** An operator might delete a branch while still wanting to keep the bucket contents (e.g., to migrate them to another branch). → Mitigation: the existing protected-branch list (`app`/`beta`/`alpha`/`dev`) already protects shared infra. Branch deletion is treated as "I'm done with this branch and everything it owns" — explicitly aligned with this change. The workflow summary documents what was destroyed for audit/recovery decisions.

- **[Risk] Race condition between pre-cleanup and an in-flight deploy.** The existing `concurrency` group requirement (D-Serialize against any in-flight deploy) covers this — the cleanup waits for any active deploy on the same branch.

- **[Trade-off] Aurora teardown integration is deferred.** If `robust-aurora-cluster-teardown` doesn't land before this change, the workflow uses an interim inline-CLI shape (D4). That works but is messier and might be revised when the other change lands. → Accepted: this change is independently useful even without Aurora cleanup, since S3+ECR cover the bulk of real failures.

- **[Trade-off] No retry on `delete-stack` failure.** If something unexpected blocks deletion after pre-cleanup, the workflow fails red and the operator investigates manually. → Accepted: silent retry papers over real signals. We want NEW failure classes to be visible so we can handle them in future iterations.

## Migration Plan

- **Source change**: edit `.github/workflows/cleanup-on-branch-delete.yml` to add the three new steps before the existing `delete-stack` call.
- **No AWS state migration needed.** This change does not modify any existing AWS resource. It only changes workflow behavior on future branch deletions.
- **Backout**: revert the workflow edit. The pre-existing cleanup behavior is restored.
- **Validation**: push a feature branch, deploy it, delete the branch, observe a clean teardown. Check the workflow summary lists every resource that was emptied/deleted. Verify the bootstrap stack's resources (TemplatesBucket, WebECRRepository, KMS keys) are NOT in the enumerated list.

## Open Questions

- **Q1**: When `robust-aurora-cluster-teardown` defines a CFN custom resource, does that resource get added to ALL env stacks (so every cluster has force-teardown wired in)? Or only opt-in per cluster? → Defer to that proposal.
- **Q2**: Should the enumeration also walk into stacks created by `AWS::Serverless::*` / SAM transforms (which produce nested stacks under the hood)? → Likely yes, since `list-stack-resources` returns them as `AWS::CloudFormation::Stack`. To verify in implementation.
- **Q3**: Bucket lifecycle policy — would adding `LifecycleConfiguration` rules that expire all objects after 1 day make this whole change moot for buckets? → Probably no — env-stack lifetimes are short enough that 1-day expiration would delete in-use content. Pre-cleanup at delete time is the right pattern.
