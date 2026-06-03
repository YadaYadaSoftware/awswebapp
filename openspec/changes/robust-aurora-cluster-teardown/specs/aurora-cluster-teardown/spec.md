## ADDED Requirements

### Requirement: A regional Lambda force-drains stuck Aurora clusters on stack delete

The `bootstrap` stack SHALL deploy an `AWS::Lambda::Function` named `AuroraClusterDeleteHandler` in **every** region (both primary and replica — not gated on `IsPrimary`), with an inline Python 3.12 implementation that, when invoked as a CloudFormation custom resource with `RequestType: Delete`, force-drains a specified Aurora DB cluster.

The handler SHALL perform the following sequence on `Delete`:

1. Call `DescribeDBClusters` for the requested `ClusterIdentifier`. If the cluster does not exist (`ClusterNotFoundFault`), signal CFN SUCCESS immediately.
2. If the cluster has `DeletionProtection: true`, call `ModifyDBCluster` to set it to `false` with `ApplyImmediately: true`. Tolerate errors here (e.g., cluster in non-modifiable state); proceed regardless.
3. For each `DBClusterMember`, call `DeleteDBInstance` with `SkipFinalSnapshot: true`.
4. Poll `DescribeDBClusters` every ~30 seconds, waiting for the cluster to leave any of: `backing-up`, `creating`, `modifying`, `configuring-iam-database-auth`. Time out after the Lambda's configured timeout (15 minutes).
5. Call `DeleteDBCluster` with `SkipFinalSnapshot: true`.
6. Poll until the cluster returns `ClusterNotFoundFault`.
7. Signal CFN SUCCESS.

On `RequestType: Create` and `RequestType: Update`, the handler SHALL signal CFN SUCCESS immediately without any RDS API call.

Any uncaught exception SHALL be returned to CFN as a FAILED signal with the exception's message, so that the resulting `DELETE_FAILED` event carries the actual root cause rather than a downstream symptom (e.g. "subnet group is still using cluster X").

#### Scenario: Cluster already gone — fast path
- **WHEN** the Lambda is invoked with `RequestType=Delete` and the named cluster no longer exists in RDS
- **THEN** the Lambda signals CFN SUCCESS within a few seconds, having made one `DescribeDBClusters` call

#### Scenario: Cluster stuck in `backing-up` is force-drained
- **WHEN** the Lambda is invoked with `RequestType=Delete` and the cluster is in state `backing-up`, with the backup unable to complete (e.g., KMS key access is broken)
- **THEN** the Lambda polls until the timeout, then calls `DeleteDBCluster` with `SkipFinalSnapshot=true`, then polls until the cluster is gone, then signals SUCCESS — OR if the cluster won't reach a deletable state within the Lambda timeout, signals FAILED with a message naming the stuck state

#### Scenario: Cluster with active instances
- **WHEN** the Lambda is invoked with `RequestType=Delete` and the cluster has one or more `DBClusterMember` instances
- **THEN** the Lambda deletes each member instance via `DeleteDBInstance` before attempting `DeleteDBCluster`

#### Scenario: DeletionProtection blocks delete
- **WHEN** the cluster has `DeletionProtection: true` when the Lambda runs
- **THEN** the Lambda first calls `ModifyDBCluster` to disable `DeletionProtection` (`ApplyImmediately: true`), then proceeds with delete

#### Scenario: Create event is a no-op
- **WHEN** the Lambda is invoked with `RequestType=Create`
- **THEN** the Lambda makes no RDS API calls and signals CFN SUCCESS immediately

#### Scenario: Update event is a no-op
- **WHEN** the Lambda is invoked with `RequestType=Update`
- **THEN** the Lambda makes no RDS API calls and signals CFN SUCCESS immediately

### Requirement: Lambda ARN is published via SSM Parameter Store

The `bootstrap` stack SHALL publish the Lambda's ARN to AWS Systems Manager Parameter Store at the well-known path `/taskmanager/lambda/aurora-cluster-delete-handler-arn`, in the same region as the Lambda. The SSM parameter SHALL NOT have `DeletionPolicy: Retain` during this change's setup phase (consistent with the parent `centralize-aurora-kms-keys` change's iteration-friendliness rationale).

The bootstrap stack SHALL also expose the Lambda ARN as a stack output `AuroraClusterDeleteHandlerArn` for diagnostic visibility.

The deploy workflow SHALL read this SSM parameter at deploy time and pass the value as the `AuroraClusterDeleteHandlerArn` stack parameter when deploying master.template, following the same pattern as the existing KMS key ARN lookup.

#### Scenario: SSM parameter populated on bootstrap deploy
- **WHEN** the `bootstrap` stack is deployed in any region
- **THEN** the SSM parameter `/taskmanager/lambda/aurora-cluster-delete-handler-arn` exists in that region and its value is the ARN of the just-created Lambda function

#### Scenario: Workflow reads Lambda ARN via SSM
- **WHEN** the deploy workflow runs on a `master.template` branch (`dev`/`alpha`/`beta`/`app`)
- **THEN** the workflow reads `/taskmanager/kms/{prod,nonprod}/aurora-key-arn` AND `/taskmanager/lambda/aurora-cluster-delete-handler-arn` in `matrix.region`, exporting both as env vars (`BOOTSTRAP_KMS_KEY_ARN` and `AURORA_CLUSTER_DELETE_HANDLER_ARN`)

### Requirement: Lambda IAM permissions are minimum-necessary and scoped to `taskmanager-*`

The Lambda's execution role's policy SHALL grant only:
- `rds:DescribeDBClusters`, `rds:DescribeDBInstances` on `Resource: "*"` (the Describe APIs do not support resource-level perms).
- `rds:DeleteDBCluster`, `rds:ModifyDBCluster` on `arn:aws:rds:${Region}:${Account}:cluster:taskmanager-*` only.
- `rds:DeleteDBInstance` on `arn:aws:rds:${Region}:${Account}:db:taskmanager-*` only.
- `kms:DescribeKey` on `Resource: "*"` (used for log enrichment when investigating KMS-related cluster issues).
- CloudWatch Logs (`logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents`) on the Lambda's own log group only.

The Lambda MUST NOT have permission to modify or delete clusters that do not match the `taskmanager-*` naming pattern.

#### Scenario: Lambda cannot delete unrelated clusters
- **WHEN** a hypothetical invocation of the Lambda passes `ClusterIdentifier=production-customer-data` (not `taskmanager-*`)
- **THEN** the `DeleteDBCluster` call returns `AccessDenied` because the IAM policy's resource constraint excludes the cluster ARN

#### Scenario: Lambda can delete taskmanager clusters
- **WHEN** the Lambda is invoked for a cluster named `taskmanager-dev`, `taskmanager-app`, `taskmanager-alpha-secondary`, or any other `taskmanager-*`
- **THEN** the IAM permissions allow the delete operations

### Requirement: Custom resource in db.template depends on AuroraCluster

The `db.template` SHALL declare a `Custom::AuroraClusterDrainOnDelete` resource that:
- has `ServiceToken: !Ref AuroraClusterDeleteHandlerArn` (the value threaded through stack parameters from the workflow's SSM lookup),
- has `ClusterIdentifier: !Ref AuroraCluster`,
- declares `DependsOn: AuroraCluster`.

The `DependsOn` ensures CFN's deletion order places this custom resource's `Delete` event BEFORE the native `AWS::RDS::DBCluster` resource's own delete. The Lambda drains the cluster; CFN's subsequent `AWS::RDS::DBCluster` delete then finds the cluster gone and reports success without contributing to a deadlock.

The custom resource MUST NOT have any other side effects on stack create or stack update.

#### Scenario: Stack delete force-drains the cluster
- **WHEN** an env stack containing this custom resource is deleted (via the branch-delete workflow, manual `aws cloudformation delete-stack`, or CFN rollback after failed CREATE)
- **THEN** the Lambda is invoked with `RequestType=Delete` and `ClusterIdentifier` matching the actual cluster; the cluster is fully drained (instances + cluster removed); the subsequent CFN-managed `AWS::RDS::DBCluster` delete reports success; the `AuroraDBSubnetGroup` delete proceeds without "cluster is still using it" errors

#### Scenario: Stack create or update is unaffected
- **WHEN** an env stack containing this custom resource is created or updated
- **THEN** the Lambda is invoked with `RequestType=Create` or `Update`; the Lambda signals SUCCESS immediately; no Aurora API calls are made; the deploy duration is increased by less than 10 seconds (cold-start + signal)

### Requirement: Parameter chain wires the Lambda ARN from workflow to db.template

The `master.template`, `backend.template`, and `db.template` SHALL each declare an `AuroraClusterDeleteHandlerArn` parameter, passing it through nested stack invocations until it reaches the custom resource's `ServiceToken`. The workflow sources this value from SSM and passes it as a parameter override when deploying `master.template`.

#### Scenario: Parameter propagation
- **WHEN** the deploy workflow runs for `dev` and sets `AuroraClusterDeleteHandlerArn=<lambda arn from SSM>`
- **THEN** the value propagates: workflow → master.template → backend.template → db.template → `AuroraClusterDrainOnDelete.ServiceToken`. CloudFormation's stack-update succeeds and the custom resource is created with the Lambda ARN as its `ServiceToken`.
