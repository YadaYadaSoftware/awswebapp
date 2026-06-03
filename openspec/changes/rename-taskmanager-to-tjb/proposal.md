## Why

The .NET project assemblies in this repo standardized on the `Tjb.*` prefix (`Tjb.Web`, `Tjb.Api`, `Tjb.Data`, `Tjb.Migrations`, `Tjb.Shared`, `Tjb.UiTests`) but the rest of the codebase still uses `TaskManager` / `Taskmanager` / `taskmanager` in **51 files** spread across CloudFormation templates, OpenSpec docs, the GitHub Actions workflow, src/docs, several C# source files, and assorted appsettings. AWS resources carry `taskmanager-*` naming (KMS aliases `alias/taskmanager-aurora-{prod,nonprod}`, SSM paths `/taskmanager/kms/*`, Aurora cluster identifiers `taskmanager-<branch>-global-cluster`, Secrets Manager paths `taskmanager/database/regional/*`, IAM policy resource scopes `arn:aws:rds:*:*:cluster:taskmanager-*`). The dual naming creates real friction:

- New contributors have to learn that `Tjb` and `TaskManager` refer to the same thing.
- The in-flight [`make-deployment-stack-reusable`](../make-deployment-stack-reusable/proposal.md) change has to special-case both names while parameterizing.
- `grep`/`Find in Files` for either string returns half the actual occurrences.
- Documentation and AWS console don't agree with each other.

Pick one. The .NET assemblies already chose `Tjb`; the AWS / docs / OpenSpec side hasn't caught up. This change brings the rest into alignment.

## What Changes

- **Case-aware text replacement across the entire repo**, mapping:
  - `TaskManager` → `Tjb` (PascalCase, mostly in C# / appsettings / docs)
  - `Taskmanager` → `Tjb` (the single-capital form the user used in the request)
  - `Task Manager` → `Tjb` (spaced form, occasional in prose)
  - `taskmanager` → `tjb` (lowercase, mostly in AWS resource names and SSM paths)
  - File names containing `TaskManager` (e.g., `scripts/Connect-TaskManagerDB.ps1` if present) → renamed with the corresponding case mapping.
- Touched file types: `*.template` (CFN YAML), `*.yml` / `*.yaml`, `*.md`, `*.cs`, `*.json` (appsettings only — NOT build-artifact JSON), `*.csproj`, `*.ps1`, `*.sh`. Build artifacts under `obj/` and `bin/` are skipped (regenerated on next build).
- **BREAKING (AWS-resource-level):** AWS resource names that include `taskmanager` change to `tjb`:
  - KMS aliases: `alias/taskmanager-aurora-{prod,nonprod}` → `alias/tjb-aurora-{prod,nonprod}` (alias *names*; underlying key ARNs unchanged).
  - SSM Parameter Store paths: `/taskmanager/kms/{prod,nonprod}/aurora-key-arn` → `/tjb/kms/{prod,nonprod}/aurora-key-arn`. Workflow's SSM lookup reads the new path.
  - Aurora Global Cluster identifier: `taskmanager-<branch>-global-cluster` → `tjb-<branch>-global-cluster`. **This forces a new cluster to be created** because cluster identifiers are immutable; existing global clusters need snapshot + recreate-on-new-name OR be left in place and renamed via a future planned migration.
  - Aurora regional cluster identifiers: same shape; same recreate-required caveat.
  - Secrets Manager paths: `taskmanager/database/regional/*` → `tjb/database/regional/*`.
  - IAM policy resource scopes inside `DeploymentPolicy` / `DeploymentPolicy-prod` / `SharedLambdaExecutionRole`: `arn:aws:rds:*:*:cluster:taskmanager-*` → `arn:aws:rds:*:*:cluster:tjb-*`.
  - The `cf-templates-{account}-{region}` S3 bucket is externally-managed and doesn't carry the project name in its name — unaffected.
- The 5 in-flight OpenSpec changes (`centralize-aurora-kms-keys`, `make-deployment-stack-reusable`, `move-shared-lambda-role-to-bootstrap`, `robust-aurora-cluster-teardown`, `shift-secondary-region-to-us-east-2`) all reference the old names in their proposal/design/spec/tasks files. They get scrubbed in this change too.
- The existing capability specs that reference resource names by their string (e.g., `aurora-kms-key-management`'s scenarios mentioning `alias/taskmanager-aurora-prod`) get delta updates to reflect the new names.

## Capabilities

### New Capabilities

- `project-naming-convention`: Establishes the invariant that this project uses only `Tjb` (PascalCase) and `tjb` (lowercase) as identifiers; no occurrences of `TaskManager`, `Taskmanager`, `Task Manager`, or `taskmanager` exist anywhere in committed files. The capability is testable via grep.

### Modified Capabilities

- `aurora-kms-key-management` (in `centralize-aurora-kms-keys`): scenarios that name KMS aliases / SSM paths get updated to use `tjb`. The Allow/Deny statement principal references in the prod key policy don't change (they reference IAM users by name like `GitHubActionsUser`, which doesn't contain `taskmanager`).
- `multi-region-deployment-topology` (in `shift-secondary-region-to-us-east-2`): scenarios mentioning `taskmanager-<branch>-global-cluster` get updated to `tjb-<branch>-global-cluster`.
- `aurora-cluster-teardown` (in `robust-aurora-cluster-teardown`): scenarios mentioning `taskmanager-*` IAM resource scoping get updated to `tjb-*`.
- `shared-lambda-role-management` (in `move-shared-lambda-role-to-bootstrap`): role name `taskmanager-shared-lambda-execution-role` becomes `tjb-shared-lambda-execution-role`. Same for SSM path `/taskmanager/iam/shared-lambda-role-arn` → `/tjb/iam/shared-lambda-role-arn`.
- `reusable-deployment-stack` (in `make-deployment-stack-reusable`): the proposal's grep audit acceptance criteria (`no taskmanager literal remains`) becomes `no taskmanager or TaskManager literal remains`. The `ProjectName` default / TaskManager-specific value becomes `tjb` / `Tjb`.

(These delta specs are part of *this* change — they don't depend on the underlying changes having landed first. Whichever change archives first wins; the other resolves the delta as a no-op.)

## Impact

**Files touched (51+ source files plus generated specs):**
- 10 infrastructure CloudFormation templates (all of `infrastructure/*.template`)
- 1 workflow file (`.github/workflows/zbuild.yml`)
- 14+ documentation files (`src/docs/*.md`)
- 4 C# source files (`src/Tjb.Api/Services/DatabaseMigrationService.cs`, `src/Tjb.Migrations/Program.cs`, `src/Tjb.Data/TjbDbContext.cs`, `src/Tjb.UiTests/TestReporter.cs`, possibly `src/Tjb.UiTests/BaseTest.cs` and `src/Tjb.Api/Program.cs`)
- Several `appsettings.json` / `appsettings.Development.json` files
- All 5 in-flight OpenSpec change directories (proposal.md / design.md / specs/ / tasks.md)
- File renames: any file whose name contains `TaskManager` (audited during Phase 1)

**Build artifacts NOT touched** (regenerated on next build):
- `**/obj/**/*.AssemblyInfo.cs`
- `**/bin/**/*`

**AWS resources requiring action:**
- **Bootstrap stack** must be redeployed to apply the new KMS alias names + SSM path names. CloudFormation drops the old `alias/taskmanager-*` aliases and creates new `alias/tjb-*` aliases pointing at the same underlying KMS key (key ARNs unchanged).
- **SSM parameters** at the old paths (`/taskmanager/kms/*`) need to be manually deleted after the new paths are populated (CFN won't delete them since the new resource has a different `Name`).
- **Aurora cluster identifiers** change → existing env-stack clusters need to be recreated. For non-prod (`dev`/`alpha`/`beta`): the existing recreate procedure from `centralize-aurora-kms-keys/tasks.md` Phase 3 covers this (drop env stack → redeploy → restore from snapshot). For prod (`app`): the existing maintenance-window cutover from Phase 4 applies.
- **Secrets Manager paths** change → existing secrets at `taskmanager/database/regional/*` need to be either renamed or recreated. Since the deploy workflow auto-creates them per-env-stack, this happens naturally when env stacks redeploy.

**Workflow:**
- `.github/workflows/zbuild.yml`'s "Lookup bootstrap KMS key from SSM" step needs to read the new SSM path (`/tjb/kms/{prod,nonprod}/aurora-key-arn`).

**Out of scope (explicitly):**
- File renames that change file paths in a way that breaks `git blame` history (we accept the cost; git follows renames).
- Renaming the GitHub repository itself (`YadaYadaSoftware/awswebapp`) — the org name and repo URL stay.
- Renaming GitHub Actions secrets (e.g., `AWS_ACCESS_KEY_ID`) — generic names, no rename needed.
- Renaming the IAM users (`GitHubActionsUser`, `GitHubActionsUserProd`) — generic names that don't contain `TaskManager`.
- Renaming the `cf-templates-{account}-{region}` S3 bucket — externally managed, not in our naming.
- Renaming the GitHub Actions branch model (`app`/`beta`/`alpha`/`dev`) — these are branch names, not project names.
- Renaming the database name itself (e.g., the EF Core context creates a database named after the branch — `dev`, `alpha`, etc. — no `taskmanager` involved).
