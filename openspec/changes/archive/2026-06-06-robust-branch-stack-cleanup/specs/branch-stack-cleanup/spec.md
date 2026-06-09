## ADDED Requirements

### Requirement: Enumerate the full stack tree before any destructive action

The cleanup workflow SHALL call `aws cloudformation list-stack-resources` against the target stack, recursing into every resource of type `AWS::CloudFormation::Stack` (the nested-stack children) to build a complete inventory of resources owned by the stack tree being deleted. This enumeration SHALL complete successfully before any destructive operation (object deletion, image deletion, cluster deletion, or `delete-stack`) is performed.

If enumeration fails for any reason — pagination failure, permissions error, transient AWS API issue — the workflow SHALL abort with no destructive actions taken, write the failure to `$GITHUB_STEP_SUMMARY`, and exit non-zero.

The enumeration SHALL be written to `$GITHUB_STEP_SUMMARY` BEFORE any destructive step runs, so the operator can see what the workflow intends to touch.

#### Scenario: Full tree enumerated
- **WHEN** the target stack `feature-foo-appcloud-systems` contains nested stacks `BackendStack` and `ApplicationStack`, each with its own nested children
- **THEN** the workflow lists all resources from all stacks in the tree (top-level + every nested) and prints them to the workflow summary before any other step runs

#### Scenario: Enumeration failure
- **WHEN** `aws cloudformation list-stack-resources` fails (e.g., transient throttling that exhausts retries)
- **THEN** the workflow aborts immediately, no buckets are emptied, no images are deleted, no clusters are killed, no `delete-stack` is issued, and the workflow exits non-zero with the failure summarized

### Requirement: Pre-empty every S3 bucket owned by the stack tree

For every `AWS::S3::Bucket` resource enumerated in the stack tree, the cleanup workflow SHALL delete all objects, all object versions, and all delete markers from the bucket before calling `delete-stack`. The implementation SHALL use `aws s3api list-object-versions` with pagination and `aws s3api delete-objects` in batches of 1000 (the API's maximum).

The workflow SHALL operate ONLY on buckets that appear as `AWS::S3::Bucket` resources in the enumerated stack tree. Buckets owned by other stacks (notably the bootstrap stack's `TemplatesBucket`) SHALL NOT be touched, even if they exist in the same account and region.

#### Scenario: Versioned bucket with objects
- **WHEN** an env-stack-owned bucket contains 500 objects, 200 versions of those objects, and 50 delete markers
- **THEN** the workflow deletes all 750 entries via paginated `s3api delete-objects` calls before `delete-stack` is issued. After this step, `list-object-versions` against the bucket returns zero entries

#### Scenario: Bucket owned by another stack is not touched
- **WHEN** the account contains a bucket `991795635857-appcloud-systems-us-east-1` owned by the bootstrap stack (not by the env stack being deleted)
- **THEN** the cleanup workflow does NOT call `delete-objects` against that bucket, and the bucket's contents remain unchanged

#### Scenario: Empty bucket already
- **WHEN** an env-stack-owned bucket has no objects
- **THEN** the workflow records "0 objects emptied" in the summary and proceeds without error

### Requirement: Pre-delete every image in ECR repositories owned by the stack tree

For every `AWS::ECR::Repository` resource enumerated in the stack tree, the cleanup workflow SHALL list all images and delete them via `aws ecr batch-delete-image` (paginated in chunks of 100 — the API's per-call maximum) before calling `delete-stack`. The repository itself is then deleted by `delete-stack` in the normal CFN flow.

The workflow SHALL operate ONLY on repositories that appear as `AWS::ECR::Repository` resources in the enumerated stack tree. Repositories owned by other stacks (notably the bootstrap stack's `WebECRRepository`) SHALL NOT be touched.

#### Scenario: Repository with images
- **WHEN** an env-stack-owned ECR repository contains 12 images
- **THEN** the workflow deletes all 12 images via one or more `batch-delete-image` calls before `delete-stack` is issued

#### Scenario: Repository owned by another stack is not touched
- **WHEN** the account contains an ECR repository `appcloud-systems` owned by the bootstrap stack (not by the env stack being deleted)
- **THEN** the cleanup workflow does NOT call `batch-delete-image` against that repository, and the repository's images remain unchanged

#### Scenario: Repository already empty
- **WHEN** an env-stack-owned ECR repository contains zero images
- **THEN** the workflow records "0 images deleted" in the summary and proceeds without error

### Requirement: Pre-tear-down every Aurora cluster in the stack tree

For every `AWS::RDS::DBCluster` resource enumerated in the stack tree, the cleanup workflow SHALL invoke the force-teardown mechanism defined by the [`robust-aurora-cluster-teardown`](../../robust-aurora-cluster-teardown/proposal.md) change before calling `delete-stack`.

If `robust-aurora-cluster-teardown` has not yet shipped at the time this change is applied, the workflow SHALL use an interim implementation that calls `aws rds delete-db-cluster --skip-final-snapshot` directly for each cluster and waits for `db-cluster-deleted`. The interim implementation MAY be replaced later (without further capability changes) when `robust-aurora-cluster-teardown` lands a custom-resource or Lambda mechanism.

The workflow SHALL operate ONLY on clusters that appear as `AWS::RDS::DBCluster` resources in the enumerated stack tree.

#### Scenario: Cluster stuck in backing-up
- **WHEN** an env-stack-owned Aurora cluster is in `backing-up` state and the deploy workflow's normal `delete-stack` flow would fail with "DBCluster is in state: backing-up"
- **THEN** the cleanup workflow's Aurora force-teardown step transitions the cluster to `deleting` and then `deleted` before `delete-stack` is issued

#### Scenario: Cluster already deletable
- **WHEN** an env-stack-owned Aurora cluster is in `available` state (no backup in progress)
- **THEN** the force-teardown step runs, the cluster transitions to `deleting`, and `delete-stack` proceeds normally

#### Scenario: Cluster owned by another stack is not touched
- **WHEN** the account contains an Aurora cluster owned by a stack other than the one being deleted
- **THEN** the cleanup workflow does NOT call `delete-db-cluster` against that cluster

### Requirement: Halt before `delete-stack` if any pre-cleanup step fails

If any of the pre-cleanup steps (enumerate, empty bucket, delete images, tear-down cluster) fails for any resource, the workflow SHALL exit non-zero before issuing `delete-stack`. The half-deleted state is preserved for operator inspection.

The workflow summary SHALL still be written, identifying which resource(s) failed and at which step.

#### Scenario: Bucket emptying fails
- **WHEN** `s3api delete-objects` returns an error for one of the buckets in the stack tree (e.g., access denied because of a bucket policy)
- **THEN** the workflow does NOT call `delete-stack`. The summary records the bucket name, the operation that failed, and the AWS error. The workflow exits non-zero

#### Scenario: All pre-cleanup succeeds, delete-stack still fails
- **WHEN** every pre-cleanup step succeeds but `delete-stack` then fails with `DELETE_FAILED` (a new class of blocker)
- **THEN** the workflow exits non-zero per the existing "Delete the stack and wait" requirement. The summary records what pre-cleanup did successfully PLUS the unexpected `DELETE_FAILED` reason, so the operator has the full picture
