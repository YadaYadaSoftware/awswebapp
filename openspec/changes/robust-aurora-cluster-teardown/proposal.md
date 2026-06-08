## Why

CloudFormation's default `AWS::RDS::DBCluster` delete handler can't recover when a cluster is in a transient state that blocks normal deletion — most commonly `backing-up`, `inaccessible-encryption-credentials`, or `modifying`. When the env-stack delete is triggered (by branch deletion, manual `aws cloudformation delete-stack`, or CFN rollback after a failed CREATE), the cluster's delete call fails or hangs forever, CFN moves on to delete dependent resources (subnet group, security group, etc.), and those fail because the cluster still exists. The stack ends in `DELETE_FAILED` and no further deletion can proceed without manual intervention.

We hit this directly during the [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md) deploy: a misconfigured prod KMS key policy put an Aurora cluster in `inaccessible-encryption-credentials`, then an automated snapshot was triggered and got stuck in `backing-up` forever, then five `aws cloudformation delete-stack` attempts over 16 days all failed with *"Cannot delete the subnet group … because at least one database cluster … is still using it"*. Manual recovery required: stop the broken backup, run `aws rds delete-db-cluster --skip-final-snapshot` directly, then re-trigger the stack delete. The same scenario can recur from any number of root causes (transient KMS issue, RDS internal error, network partition during snapshot, etc.). The robustness needs to live in the stack itself.

## What Changes

- Add a Lambda function `AuroraClusterDeleteHandler` to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) that, on demand, force-deletes an Aurora DB cluster: clears `DeletionProtection` if set, deletes all member DB instances first, then calls `delete-db-cluster --skip-final-snapshot`, and polls until the cluster is fully gone (or returns a clear error if AWS itself rejects the delete). Lambda is created once per region (always — not gated by `IsPrimary`, since each region's clusters need their own regional Lambda).
- Publish the Lambda's ARN at SSM parameter `/taskmanager/lambda/aurora-cluster-delete-handler-arn` in each region (parallel to how the KMS key ARNs are published).
- Add an IAM role for the Lambda with minimum-necessary permissions: `rds:DescribeDBClusters`, `rds:DescribeDBInstances`, `rds:DeleteDBCluster`, `rds:DeleteDBInstance`, `rds:ModifyDBCluster` (to clear deletion-protection), `kms:DescribeKey`, basic CloudWatch logging.
- Add a custom resource `AuroraClusterDrainOnDelete` to [infrastructure/db.template](../../../infrastructure/db.template) that invokes the Lambda. The custom resource's only behavior is on `Delete`: it tells the Lambda which cluster to drain. On `Create`/`Update` it's a no-op (signals success immediately). The custom resource declares `DependsOn: AuroraCluster`, so CFN deletes the custom resource **first** during stack delete — the Lambda force-drains the cluster, then CFN's `AuroraCluster` delete that runs next sees the cluster already gone and reports success.
- The deploy workflow's KMS-lookup step ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) gains a parallel SSM lookup for the Lambda ARN, exported as `AURORA_CLUSTER_DELETE_HANDLER_ARN`. Master/backend/db templates take a new `AuroraClusterDeleteHandlerArn` parameter passed through the chain into the custom resource's `ServiceToken`.

## Capabilities

### New Capabilities

- `aurora-cluster-teardown`: How env stacks ensure their Aurora cluster is fully and forcibly deleted before CFN moves on to delete dependent resources (subnet group, security group, etc.). Covers the Lambda contract, the custom-resource integration, and the failure semantics (what happens if AWS itself rejects the force-delete).

### Modified Capabilities

_None._ The `aurora-kms-key-management` spec describes the KMS key lifecycle but not how Aurora clusters are torn down; no overlap. The `branch-stack-cleanup` spec describes the on-delete workflow but only at the "delete the stack" level — it doesn't constrain cluster-specific deletion behavior.

## Impact

**Infrastructure templates (modified):**
- [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — adds the `AuroraClusterDeleteHandler` Lambda + its IAM role + its log group + an SSM parameter exposing the Lambda's ARN. Always created (both primary and replica region — each region's clusters need their own regional handler). Adds 1 new output: `AuroraClusterDeleteHandlerArn`.
- [infrastructure/db.template](../../../infrastructure/db.template) — adds the `AuroraClusterDrainOnDelete` custom resource (typed `Custom::AuroraClusterDrainOnDelete`). New `AuroraClusterDeleteHandlerArn` parameter consumed as the custom resource's `ServiceToken`.
- [infrastructure/backend.template](../../../infrastructure/backend.template) — adds `AuroraClusterDeleteHandlerArn` parameter, passes through to `DbStack`.
- [infrastructure/master.template](../../../infrastructure/master.template) — adds `AuroraClusterDeleteHandlerArn` parameter, passes through to `BackendStack`.

**Workflow:**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — adds a new "Lookup Aurora cluster delete handler ARN from SSM" step (parallel to the existing KMS lookup) that exports `AURORA_CLUSTER_DELETE_HANDLER_ARN` as a `$GITHUB_ENV` variable. The `Set parameter overrides` step adds `AuroraClusterDeleteHandlerArn=${AURORA_CLUSTER_DELETE_HANDLER_ARN}` to the master-template params.

**Existing env stacks (`dev`, `alpha`, `beta`, `app`):**
- First post-change deploy will create the new custom resource. The custom resource is a no-op on Create, so no behavioral change at deploy time.
- Subsequent stack deletes (branch cleanup or manual) will force-drain the cluster instead of getting stuck on transient cluster states. **This is the only behavior change visible to operators** — failed stack-deletes should become rare/non-existent for Aurora-cluster reasons.

**Out of scope (explicitly):**
- Non-Aurora RDS engines (e.g., standalone Postgres). The Lambda's logic is tied to Aurora's cluster + instances shape; if we ever add a standalone DBInstance, that needs its own handler.
- Other AWS resources that can also cause stack-delete deadlocks (ENIs attached to Fargate tasks, ALBs with active connections, etc.). Tracked-but-not-this-change.
- Modifying CFN's default behavior at the AWS service level (we can't; AWS owns the handler). We work around it via the custom resource.
- Snapshot-on-delete behavior. The handler always uses `--skip-final-snapshot` because the design intent is teardown; a separate "graceful shutdown with snapshot" handler would be a different change.
