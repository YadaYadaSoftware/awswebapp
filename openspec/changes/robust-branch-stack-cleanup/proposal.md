## Why

The existing `branch-stack-cleanup` workflow is fail-fast: it calls `aws cloudformation delete-stack` and surfaces `DELETE_FAILED` as a red CI build. In practice, env-stack deletion fails frequently because CloudFormation cannot delete resources that hold state:

- An `AWS::S3::Bucket` with objects in it.
- An `AWS::ECR::Repository` with images in it (unless `ForceDelete: true` was set at create time, which the current templates do not set on env-stack-owned repos).
- An `AWS::RDS::DBCluster` stuck in `backing-up` state (current pain point — the `app` env stack has been stuck for 16 days).

The current behavior dumps the operator into manual cleanup mode: hand-emptying the bucket, hand-deleting ECR images, hand-killing the Aurora cluster, then retrying `delete-stack`. This change automates that pre-cleanup so branch deletions are a one-click operation again.

## What Changes

- **ADDED** requirement: pre-empty any `AWS::S3::Bucket` owned by the stack tree before `delete-stack`. Implementation enumerates the stack (and its nested stacks recursively) via `describe-stack-resources`, identifies every bucket, and deletes all objects + versions + delete-markers via paginated `s3api delete-objects`.
- **ADDED** requirement: pre-delete all images in any `AWS::ECR::Repository` owned by the stack tree before `delete-stack`. Implementation uses `ecr list-images` + `ecr batch-delete-image` with pagination.
- **ADDED** requirement: invoke the Aurora cluster force-teardown mechanism (defined by [`robust-aurora-cluster-teardown`](../robust-aurora-cluster-teardown/proposal.md)) for every `AWS::RDS::DBCluster` in the stack tree. This change does not define HOW the Aurora teardown works — that lives in the sibling proposal. It only requires the workflow to invoke it as part of the pre-deletion pipeline.
- **ADDED** requirement: enumerate-before-destroy. The workflow runs `describe-stack-resources` (recursing into nested stacks) and prints the full list of resources to `$GITHUB_STEP_SUMMARY` before any destructive action. If enumeration fails, the workflow aborts without touching anything.
- **ADDED** requirement: safety scope. The workflow operates ONLY on resources that are CFN-tracked members of the stack tree being deleted. It never touches buckets, repos, or clusters that belong to other stacks (notably the bootstrap stack's `TemplatesBucket`, `WebECRRepository`, and Aurora KMS keys).
- The "Delete the stack and wait for terminal state" requirement is not modified — `DELETE_FAILED` after pre-cleanup is still a workflow failure (because the pre-cleanup should have addressed all known blockers; a `DELETE_FAILED` past that point indicates a NEW class of blocker worth surfacing).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `branch-stack-cleanup`: adds the four new requirements above. No existing requirement is modified or removed — this is purely additive.

## Impact

**Files touched:**
- `.github/workflows/cleanup-on-branch-delete.yml` — the bulk of the change. Add 3 new steps before the existing `delete-stack` call: (1) enumerate-resources-recursively, (2) empty-buckets-and-repos, (3) invoke-aurora-teardown.
- Possibly `scripts/cleanup/*` for the resource-enumeration logic if the workflow YAML gets unwieldy. Otherwise inline as bash.

**AWS API calls added per cleanup run:**
- `aws cloudformation list-stack-resources` (paginated; once per stack in the tree).
- `aws s3api list-objects-v2` + `list-object-versions` + `delete-objects` per bucket.
- `aws ecr list-images` + `batch-delete-image` per repository.
- Invocation of Aurora teardown mechanism per cluster.

**Existing workflow contract** (concurrency, single-region, protected-branch skip, stack-name formula, S3 templates cleanup): unchanged.

**Out of scope (deferred):**
- Forcing deletion past the 30-minute timeout. The existing requirement holds — preempt blockers, but trust the waiter to fail loudly if something unexpected blocks.
- Generic blocker discovery (e.g., Lambda layers, ENIs, custom resources). Add later if real-world failures warrant.
- Retry-on-DELETE_FAILED. The first `delete-stack` is still the only attempt. Bad pre-cleanup → bad outcome — surface it, don't paper over.
