## Why

After the [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md) change landed, [`security.template`](../../../infrastructure/security.template) shrank to a single resource: the `SharedLambdaExecutionRole` (used by Lambda functions and ECS tasks across every env stack), with a `SharedLambdaRoleArn-${BranchName}` CloudFormation export consumed by `api.template` and `web.template`. Every env stack creates its own copy of this role with **identical** policies — there's no per-env scoping in any of the inline policy documents, just a different ARN per env. That's pure duplication with operational cost (one IAM role created per env stack, one less obvious place to audit IAM permissions) and no functional benefit. The previous change tracked this as a deferred §9.1 follow-up; this is that follow-up.

## What Changes

- **BREAKING** Move `SharedLambdaExecutionRole` out of [security.template](../../../infrastructure/security.template) and into the consolidated [bootstrap.template](../../../infrastructure/bootstrap.template). The role becomes a single global IAM resource owned by `bootstrap` (gated on `IsPrimary` since IAM is global), with an explicit `RoleName: taskmanager-shared-lambda-execution-role` so its ARN is predictable across regions.
- Publish the role ARN at SSM Parameter Store path `/taskmanager/iam/shared-lambda-role-arn` in **both** regions (primary writes via `!GetAtt`, replica writes via `!Sub`'d predictable ARN — same pattern as the existing Aurora KMS key SSM publishing).
- The deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) gains a "Lookup shared Lambda role ARN from SSM" step parallel to the existing KMS lookup, exporting `SHARED_LAMBDA_ROLE_ARN` as a `$GITHUB_ENV` variable. The role ARN is added to `BASE_PARAMS` for **both** the master-template branch path (`dev`/`alpha`/`beta`/`app`) and the application-template branch path (feature branches) — both paths' nested stacks consume it.
- A new `SharedLambdaRoleArn` parameter threads through the templates:
  - [master.template](../../../infrastructure/master.template) → [backend.template](../../../infrastructure/backend.template) → (consumed at backend.template's `SharedLambdaRoleArn` output for orchestration)
  - [master.template](../../../infrastructure/master.template) → [application.template](../../../infrastructure/application.template) → [api.template](../../../infrastructure/api.template) and [web.template](../../../infrastructure/web.template)
  - [application.template](../../../infrastructure/application.template) (feature-branch path) → [api.template](../../../infrastructure/api.template) and [web.template](../../../infrastructure/web.template)
- **BREAKING** Replace the `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"` lookups in [api.template](../../../infrastructure/api.template) (1 reference) and [web.template](../../../infrastructure/web.template) (2 references) with `!Ref SharedLambdaRoleArn`. This removes the cross-stack export dependency that today links every feature branch's app stack to dev's security stack.
- **BREAKING** Delete [infrastructure/security.template](../../../infrastructure/security.template) entirely. Remove the `SecurityStack` nested-stack resource from [backend.template](../../../infrastructure/backend.template) and the `SharedLambdaRoleArn` output that referenced it. The `SharedLambdaRoleArn` output stays in backend.template and master.template (sourced from the new parameter) so any future consumer can still find it via stack outputs.

## Capabilities

### New Capabilities

- `shared-lambda-role-management`: How the per-account `SharedLambdaExecutionRole` IAM role is provisioned, published, discovered, and consumed by env-stack and feature-branch deploys. Replaces the per-env duplication established by the legacy `security.template` design with a single bootstrap-owned global role.

### Modified Capabilities

_None._ No existing capability spec describes this role today (the legacy `security.template` was never specified in OpenSpec — it predated the OpenSpec workflow on this project).

## Impact

**Infrastructure templates:**
- [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template) — adds `SharedLambdaExecutionRole` (Condition: `IsPrimary`, RoleName: `taskmanager-shared-lambda-execution-role`) + `SharedLambdaRoleArnParameter` (SSM, always created — primary uses `!GetAtt`, replica uses `!Sub`) + new output `SharedLambdaRoleArn`.
- [infrastructure/security.template](../../../infrastructure/security.template) — **deleted.**
- [infrastructure/backend.template](../../../infrastructure/backend.template) — removes the `SecurityStack` nested-stack resource and its `SharedLambdaRoleArn` output reference. Adds `SharedLambdaRoleArn` parameter, passes it through where needed.
- [infrastructure/master.template](../../../infrastructure/master.template) — adds `SharedLambdaRoleArn` parameter, passes to `BackendStack` and `ApplicationStack`.
- [infrastructure/application.template](../../../infrastructure/application.template) — adds `SharedLambdaRoleArn` parameter, passes to `ApiStack` and `WebStack`.
- [infrastructure/api.template](../../../infrastructure/api.template) — `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"` → `!Ref SharedLambdaRoleArn`.
- [infrastructure/web.template](../../../infrastructure/web.template) — same replacement (two references on different lines).

**Workflow:**
- [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — adds "Lookup shared Lambda role ARN from SSM" step after the existing KMS lookup; appends `SharedLambdaRoleArn=...` to `BASE_PARAMS` for both master- and application-template branches.

**Existing env stacks (`dev`, `alpha`, `beta`, `app`) and feature branches:**
- First post-change deploy will update each stack to: (a) drop the `SecurityStack` nested stack, (b) add a `SharedLambdaRoleArn` parameter resolving to the bootstrap-owned role, (c) update `api.template` / `web.template`'s `TaskRole`/`ExecutionRole` references to use the new parameter.
- The role ARN in api/web changes from `arn:aws:iam::ACCT:role/<env>-<random>` to `arn:aws:iam::ACCT:role/taskmanager-shared-lambda-execution-role`. ECS task definitions get re-rendered with the new ARN — Fargate tasks will be replaced (rolling deploy).

**Existing per-env Lambda roles (one per env stack):**
- They were created with `DeletionPolicy: Delete` (default), so CFN will delete them when the `SecurityStack` is removed from backend.template. Pre-existing references die with the stack; no orphans expected.

**Out of scope (explicitly):**
- Tightening the `SharedLambdaExecutionRole` policy. The IAM policy text stays identical to what `security.template` had (so behavior is unchanged for the application). A future change can scope it down — that's a different exercise.
- Splitting the role into separate Lambda-execution and ECS-task-execution roles (today it's used by both via the `AssumeRolePolicyDocument` listing both services). That's a long-standing oddity worth revisiting, but out of scope.
- Migrating other IAM-related per-env resources (none exist today; this is the last one).
