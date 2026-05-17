## Why

When a feature branch is deleted from the remote (after merge or abandonment), the CloudFormation stack it deployed (`{branch-leaf}-{processed-domain}`, e.g. `add-login-appcloud-systems`) remains in AWS indefinitely. These zombie stacks accumulate ECS services, ALBs, target groups, security groups, and Route53 records — costing money and cluttering the AWS console. Today the only cleanup path is a human running `aws cloudformation delete-stack` by hand, which is easy to forget.

## What Changes

- Add a new GitHub Actions workflow that triggers on the `delete` event when a *branch* is removed, and tears down the corresponding per-branch CloudFormation stack in `us-east-1`.
- The workflow refuses to delete stacks belonging to protected branches (`app`, `beta`, `alpha`, `dev`) — these are shared infrastructure that the master template manages.
- The workflow computes the stack name using the exact same formula as the deploy job: `{branch-leaf}-{processed-domain}` where `branch-leaf` is the last `/`-separated segment of the deleted ref and `processed-domain` is `secrets.DOMAIN_NAME` with `.` → `-`.
- After requesting deletion, the workflow waits for the stack to reach `DELETE_COMPLETE` (or surfaces `DELETE_FAILED` as a failed job).
- A workflow summary is written with the stack name, region, and final status so the run is self-documenting.
- The workflow also clears the branch's S3 prefix in the CF templates bucket (`s3://cf-templates-{account}-us-east-1/{branch-leaf}/`) so stale packaged templates don't accumulate.

## Capabilities

### New Capabilities
- `branch-stack-cleanup`: Automated teardown of per-branch CloudFormation stacks (and their staged template artifacts) when the source branch is deleted.

### Modified Capabilities
<!-- None — this is an additive workflow that does not change the existing deploy capability. -->

## Impact

- **New file**: `.github/workflows/cleanup-on-branch-delete.yml`.
- **No code changes** to application projects (`Tjb.Web`, `Tjb.Api`, etc.).
- **No infrastructure template changes** — `application.template` and `master.template` are untouched. Existing stack deletion semantics (resources without `DeletionPolicy: Retain`) apply as-is.
- **IAM**: Reuses the existing `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` secrets. The IAM principal those keys represent must already have `cloudformation:DeleteStack`, `cloudformation:DescribeStacks`, and `s3:DeleteObject`/`s3:ListBucket` on the CF templates bucket. If it does not, a one-time IAM policy expansion is required (called out in tasks.md).
- **Operational risk**: A branch can only be deleted once, so this workflow runs at most once per branch lifecycle. The protected-branch guard prevents the highest-impact failure mode (accidental teardown of `app`/`dev`).
- **Out of scope**: Cleaning up ECR images tagged with the branch name, deleting CloudWatch log groups left behind by retained resources, and reconciling already-orphaned stacks from before this workflow existed. Those can be addressed in a follow-up if needed.
