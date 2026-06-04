## 1. Implementation — workflow edits

- [ ] 1.1 Read the existing [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml) end-to-end. Identify the point at which the current workflow calls `aws cloudformation delete-stack` — every new step from this change runs BEFORE that call.
- [ ] 1.2 Decide whether to inline the new logic in workflow YAML (preferred for visibility / minimal moving parts) or factor into `scripts/cleanup/enumerate-stack-resources.sh`, `empty-buckets.sh`, etc. For this initial implementation: inline. Refactor only if YAML gets unwieldy.
- [ ] 1.3 Add `Enumerate stack resources (recursive)` step. Uses `aws cloudformation list-stack-resources --stack-name <name>` paginated, recursing into every `ResourceType == AWS::CloudFormation::Stack`. Output the full resource list to `$GITHUB_STEP_SUMMARY` as a Markdown bullet tree. Save the enumerated lists of buckets / repos / clusters to step outputs for downstream steps.
- [ ] 1.4 Add `Empty owned S3 buckets` step. For each bucket in the enumerated list, run `aws s3api list-object-versions` paginated and feed batches of up to 1000 objects/versions/markers to `aws s3api delete-objects`. Record the count per bucket in the summary.
- [ ] 1.5 Add `Delete owned ECR images` step. For each repository, `aws ecr list-images --query 'imageIds[*]'` paginated, batches of up to 100 to `aws ecr batch-delete-image`. Record the count per repository.
- [ ] 1.6 Add `Force-teardown owned Aurora clusters` step. If [`robust-aurora-cluster-teardown`](../robust-aurora-cluster-teardown/proposal.md) has shipped, invoke its mechanism. Otherwise, interim implementation: `aws rds delete-db-cluster --skip-final-snapshot --db-cluster-identifier <id>` followed by `aws rds wait db-cluster-deleted --db-cluster-identifier <id>`. Record per-cluster outcome.
- [ ] 1.7 Each pre-cleanup step MUST exit non-zero on AWS error and propagate the failure. Use `set -euo pipefail` at the top of every bash block.
- [ ] 1.8 Ensure none of the new steps touch resources outside the enumerated lists. Defensive check: before any destructive call, assert the resource ID appears in the enumeration output.

## 2. Spec + cross-references

- [ ] 2.1 Verify [openspec/specs/branch-stack-cleanup/spec.md](../../specs/branch-stack-cleanup/spec.md) is unchanged in this branch — this change ADDs requirements; it does not modify existing ones.
- [ ] 2.2 Run `openspec validate robust-branch-stack-cleanup --strict`. Must pass.
- [ ] 2.3 If [`robust-aurora-cluster-teardown`](../robust-aurora-cluster-teardown/proposal.md) is still in flight when this change applies, ensure that proposal's design references the integration point this change defines (one-line cross-ref so future readers know how they compose).

## 3. Manual validation

- [ ] 3.1 Create a feature branch. Push it. Verify a deploy creates the env stack with whatever resources are in it (Aurora cluster, possibly future buckets / repos).
- [ ] 3.2 Delete the feature branch via `git push origin --delete <branch>`. Observe the cleanup workflow run.
- [ ] 3.3 Read the workflow summary. Verify the enumeration lists every resource in the env stack tree. Verify the bootstrap stack's resources (`TemplatesBucket`, `WebECRRepository`, Aurora KMS keys) are NOT in the enumeration.
- [ ] 3.4 Verify each pre-cleanup step's count matches reality (objects emptied, images deleted, clusters torn down).
- [ ] 3.5 Verify the stack reaches `DELETE_COMPLETE` and the cleanup workflow exits green.
- [ ] 3.6 Repeat with a stack that's already in trouble (e.g., re-create the prod-cluster-stuck-in-backing-up scenario by intentionally triggering a backup right before delete). Confirm the force-teardown step handles it.

## 4. Negative testing

- [ ] 4.1 Create a feature branch with an env stack. Manually add an object to a bucket OUTSIDE the env stack (e.g., the bootstrap's `TemplatesBucket`) that shares a name pattern with the branch (e.g., a key like `feature-foo/something.json`). Delete the branch. Verify the cleanup workflow does NOT touch the bootstrap bucket's contents.
- [ ] 4.2 Simulate an enumeration failure (revoke `cloudformation:ListStackResources` on the IAM user temporarily, or use a non-existent stack name). Verify the workflow aborts before any destructive step.
- [ ] 4.3 Simulate a bucket-emptying failure (add a bucket policy denying `s3:DeleteObject` to the test bucket). Verify the workflow exits non-zero before `delete-stack` and the summary identifies the failed step.

## 5. Validate and archive

- [ ] 5.1 Run `openspec validate robust-branch-stack-cleanup --strict`. Must pass.
- [ ] 5.2 Verify each scenario in [specs/branch-stack-cleanup/spec.md](specs/branch-stack-cleanup/spec.md) (the ADDED delta) is exercised by manual or negative testing above.
- [ ] 5.3 Update [CLAUDE.md](../../../CLAUDE.md)'s section on cleanup behavior: branch deletion now does pre-cleanup of buckets / repos / clusters automatically; operators no longer need to hand-clean these before retrying a failed delete.
- [ ] 5.4 Archive the change per the experimental workflow (`/opsx:archive`). The ADDED requirements merge into [openspec/specs/branch-stack-cleanup/spec.md](../../specs/branch-stack-cleanup/spec.md).
