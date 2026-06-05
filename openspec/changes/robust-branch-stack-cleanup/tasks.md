## 1. Implementation — workflow edits

- [x] 1.1 Read the existing [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml) end-to-end. Identify the point at which the current workflow calls `aws cloudformation delete-stack` — every new step from this change runs BEFORE that call. — *New steps inserted between `Pre-check — does the stack exist?` and `Request stack deletion`.*
- [x] 1.2 Decide whether to inline the new logic in workflow YAML (preferred for visibility / minimal moving parts) or factor into `scripts/cleanup/*`. For this initial implementation: inline. — *Inlined as bash in the workflow.*
- [x] 1.3 Add `Enumerate stack resources (recursive)` step. Uses `aws cloudformation list-stack-resources` recursing into every `AWS::CloudFormation::Stack`. Output the full resource list to `$GITHUB_STEP_SUMMARY` as a Markdown bullet tree. Save enumerated buckets/repos/clusters for downstream steps. — *BFS over the stack tree; writes `buckets.txt`/`repos.txt`/`clusters.txt` in the workspace and a bullet-tree to the summary. (aws-cli v2 auto-paginates `list-stack-resources`, so one call per stack suffices.)*
- [x] 1.4 Add `Empty owned S3 buckets` step. For each bucket, `list-object-versions` paginated, batches of up to 1000 to `delete-objects`. Record per-bucket count. — *Deletes objects + versions + delete-markers in ≤1000-item batches; records count.*
- [x] 1.5 Add `Delete owned ECR images` step. For each repo, `ecr list-images` paginated, batches of up to 100 to `batch-delete-image`. Record per-repo count. — *Done (100-image batches).*
- [x] 1.6 Add `Force-teardown owned Aurora clusters` step. Interim implementation: delete member instances, clear deletion-protection, `delete-db-cluster --skip-final-snapshot`, then `wait db-cluster-deleted`. Record per-cluster outcome. — *Done as the interim CLI shape (per design D4 / spec). Will be superseded by `robust-aurora-cluster-teardown`'s mechanism when it lands.*
- [x] 1.7 Each pre-cleanup step MUST exit non-zero on AWS error and propagate the failure. Use `set -euo pipefail`. — *All four steps use `set -euo pipefail` plus an `ERR` trap that records the failure to the summary before the job halts.*
- [x] 1.8 Ensure none of the new steps touch resources outside the enumerated lists. — *Every destructive loop reads only from the enumeration's `buckets.txt`/`repos.txt`/`clusters.txt`; nothing is discovered by name/tag/pattern. Bootstrap-owned resources never appear in `list-stack-resources` for an env stack, so they are structurally excluded.*

## 2. Spec + cross-references

- [x] 2.1 Verify [openspec/specs/branch-stack-cleanup/spec.md](../../specs/branch-stack-cleanup/spec.md) is unchanged in this branch — this change ADDs requirements. — *Confirmed: `git status openspec/specs/` is clean; no existing spec modified.*
- [x] 2.2 Run `openspec validate robust-branch-stack-cleanup --strict`. Must pass. — *Passed.*
- [ ] 2.3 If `robust-aurora-cluster-teardown` is still in flight, ensure that proposal's design references the integration point this change defines. — *Deferred: `robust-aurora-cluster-teardown` is actively owned by another worktree (brother `wilhelm`). Editing its artifacts from this branch would collide; the cross-ref should be added there. This change already references it (design D4, spec Requirement "Pre-tear-down every Aurora cluster").*

## 3. Manual validation

- [ ] 3.1 Create a feature branch, push, verify a deploy creates the env stack. — *Not performed autonomously: this run does feature-branch pushes but does not exercise a create-then-delete teardown cycle.*
- [ ] 3.2 Delete the feature branch and observe the cleanup workflow run. — *Destructive + requires GitHub Actions UI observation (no `gh` available); deferred to human verification.*
- [ ] 3.3 Read the workflow summary; verify enumeration lists every env-stack resource and that bootstrap resources are absent. — *Deferred to human verification.*
- [ ] 3.4 Verify each pre-cleanup count matches reality. — *Deferred.*
- [ ] 3.5 Verify the stack reaches `DELETE_COMPLETE` and the workflow exits green. — *Deferred.*
- [ ] 3.6 Repeat with a cluster stuck in `backing-up`. — *Deferred (requires deliberately staging a stuck-backup scenario against live AWS).*

## 4. Negative testing

- [ ] 4.1 Confirm cleanup does NOT touch a bootstrap-bucket key sharing the branch name pattern. — *Deferred (destructive/manual). Note: the implementation cannot touch it by construction — it only iterates enumerated stack members, never name patterns.*
- [ ] 4.2 Simulate an enumeration failure; verify abort before any destructive step. — *Deferred (manual IAM change). `set -euo pipefail` on the enumerate step provides this behavior.*
- [ ] 4.3 Simulate a bucket-emptying failure; verify non-zero exit before `delete-stack` and a summary identifying the failed step. — *Deferred (manual). The `ERR` trap + `set -e` provide this behavior.*

## 5. Validate and archive

- [x] 5.1 Run `openspec validate robust-branch-stack-cleanup --strict`. Must pass. — *Passed.*
- [ ] 5.2 Verify each scenario in the ADDED delta is exercised by manual/negative testing above. — *Deferred to human verification (real teardown run needed).*
- [x] 5.3 Update [CLAUDE.md](../../../CLAUDE.md)'s section on cleanup behavior: branch deletion now does pre-cleanup of buckets/repos/clusters automatically. — *Done (extended the "Deleting a remote branch tears down its CloudFormation stack" bullet).*
- [ ] 5.4 Archive the change per the experimental workflow (`/opsx:archive`). — *Intentionally NOT done: this run is feature-branch-only (no merge/archive).*

## Implementation notes (autonomous run on branch `robust-branch-stack-cleanup`)

- **What shipped:** four new pre-cleanup steps in `cleanup-on-branch-delete.yml` (enumerate → empty S3 → delete ECR images → force-teardown Aurora), all gated on the same not-protected + stack-exists conditions, all `set -euo pipefail` with an `ERR`-trap summary breadcrumb, and all driven strictly by the enumeration output (safety scope D5).
- **Aurora step is interim** (inline CLI per design D4) and will be replaced by `robust-aurora-cluster-teardown`'s mechanism when that lands — no capability change required.
- **IAM caveat:** the cleanup runs as the GitHub Actions IAM user. The new calls (`cloudformation:ListStackResources`, `s3:DeleteObject(Version)`, `ecr:BatchDeleteImage`, `rds:DeleteDBInstance`/`DeleteDBCluster`/`ModifyDBCluster`) must be permitted; if not, the steps fail loudly and halt before `delete-stack` (the intended fail-safe). Granting those is an IAM/bootstrap concern outside this workflow edit.
- **Not autonomously verifiable:** sections 3–4 and 5.2 require a real create→delete teardown cycle and GitHub Actions UI observation; left unchecked for a human pass.
