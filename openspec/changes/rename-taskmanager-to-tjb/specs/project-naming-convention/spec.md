## ADDED Requirements

### Requirement: No occurrences of legacy project names in committed source files

The repository SHALL contain zero case-sensitive matches for any of `TaskManager`, `Taskmanager`, `Task Manager`, or `taskmanager` in committed source files. This applies to:

- All `*.cs` source files
- All `*.md` documentation files
- All `*.yml` / `*.yaml` workflow and config files
- All `*.template` CloudFormation files
- All `*.json` files **except** those under `obj/` or `bin/` directories (build artifacts)
- All `*.csproj`, `*.ps1`, and `*.sh` files
- All files under `openspec/` (proposals, designs, specs, tasks)

Build artifacts under `**/obj/**` and `**/bin/**` are excluded — they regenerate from source and inheriting the new names happens automatically on the next build. The `.git/` directory is excluded by convention.

#### Scenario: Grep audit returns empty
- **WHEN** the validation grep runs: `Get-ChildItem -Path . -Recurse -File -Include *.cs,*.md,*.yml,*.yaml,*.template,*.json,*.csproj,*.ps1,*.sh | Where-Object { $_.FullName -notmatch '\\(obj|bin|node_modules|\.git)\\' } | Select-String -CaseSensitive -Pattern '(TaskManager|Taskmanager|Task Manager|taskmanager)'`
- **THEN** no matches are reported

#### Scenario: New contributor reading conventions
- **WHEN** a new contributor searches the repo for `TaskManager` to understand the project
- **THEN** they find no hits and infer from the assembly names (`Tjb.Web`, `Tjb.Api`, etc.) and the universal `Tjb` / `tjb` usage that the project is named `Tjb`

### Requirement: AWS resource names use the `tjb` prefix exclusively

All AWS resources whose CloudFormation templates name them with a project prefix SHALL use `tjb-` (or `tjb/` for path-based identifiers). Specifically:

- KMS aliases: `alias/tjb-aurora-prod`, `alias/tjb-aurora-nonprod` (NOT `alias/taskmanager-aurora-*`)
- SSM Parameter Store paths under the project namespace: `/tjb/kms/{prod,nonprod}/aurora-key-arn` (NOT `/taskmanager/kms/*`)
- Aurora Global Cluster identifiers: `tjb-<branch>-global-cluster` (NOT `taskmanager-<branch>-global-cluster`)
- Aurora regional cluster identifiers follow the same prefix
- Secrets Manager paths: `tjb/database/regional/*` (NOT `taskmanager/database/regional/*`)
- IAM policy `Resource:` ARN scopes that match cluster patterns: `arn:aws:rds:*:*:cluster:tjb-*` (NOT `taskmanager-*`)

Resources whose names are intrinsically generic (IAM users `GitHubActionsUser` / `GitHubActionsUserProd`, the `cf-templates-{account}-{region}` S3 bucket, the four shared-infrastructure branches `app`/`beta`/`alpha`/`dev`) are NOT in scope for renaming — they don't carry the project prefix in the first place.

#### Scenario: KMS aliases use tjb prefix
- **WHEN** `aws kms list-aliases --region us-east-1 --query "Aliases[?starts_with(AliasName, 'alias/tjb-aurora-')]"` is run after the bootstrap stack is redeployed
- **THEN** two aliases exist: `alias/tjb-aurora-prod` and `alias/tjb-aurora-nonprod`. The query for `'alias/taskmanager-aurora-'` returns empty

#### Scenario: SSM paths use tjb namespace
- **WHEN** the deploy workflow reads `/tjb/kms/nonprod/aurora-key-arn` from SSM in `us-east-1`
- **THEN** the parameter exists and resolves to a valid KMS key ARN. The legacy path `/taskmanager/kms/nonprod/aurora-key-arn` either does not exist or has been explicitly deleted during cleanup

#### Scenario: Aurora cluster identifier uses tjb prefix
- **WHEN** a fresh `dev` env stack deploys after the rename takes effect
- **THEN** the resulting Aurora Global Cluster identifier is `tjb-dev-global-cluster` (not `taskmanager-dev-global-cluster`)

### Requirement: File names containing the legacy project name are renamed

Any file whose path or filename contains `TaskManager` (any case) SHALL be `git mv`-renamed to use `Tjb` (PascalCase) or `tjb` (lowercase) per the project's casing convention. Examples of files to audit: PowerShell scripts (`scripts/Connect-TaskManagerDB.ps1` if present), any test-data files with `TaskManager` in the name, any documentation files with `TaskManager` in the filename.

#### Scenario: No filename contains legacy name
- **WHEN** `Get-ChildItem -Path . -Recurse -File | Where-Object { $_.Name -cmatch '(TaskManager|Taskmanager|taskmanager)' }` is run from the repo root
- **THEN** no matching file paths are returned (excluding `.git/` and `obj/` and `bin/` directories)

### Requirement: OpenSpec docs in related changes are scrubbed too

The 5 in-flight OpenSpec changes (`centralize-aurora-kms-keys`, `make-deployment-stack-reusable`, `move-shared-lambda-role-to-bootstrap`, `robust-aurora-cluster-teardown`, `shift-secondary-region-to-us-east-2`) SHALL have their `proposal.md`, `design.md`, `tasks.md`, and `specs/**/*.md` files updated as part of this change. After the update, those changes' OpenSpec docs SHALL contain no `TaskManager` / `Taskmanager` / `taskmanager` references.

Where the existing change docs include narrative prose mentioning the legacy name (not just resource literals), the prose is rewritten to read naturally with `Tjb` substituted — sentences are not left mechanically broken (e.g., "TaskManager has a single bootstrap stack" becomes "Tjb has a single bootstrap stack" or, where it reads more naturally, "the project has a single bootstrap stack").

#### Scenario: OpenSpec docs scrub clean
- **WHEN** the validation grep runs against `openspec/`
- **THEN** no `TaskManager` / `Taskmanager` / `taskmanager` matches are found

#### Scenario: Each in-flight change still validates
- **WHEN** `openspec validate <change-name> --strict` runs for each of `centralize-aurora-kms-keys`, `make-deployment-stack-reusable`, `move-shared-lambda-role-to-bootstrap`, `robust-aurora-cluster-teardown`, `shift-secondary-region-to-us-east-2`, and `rename-taskmanager-to-tjb` itself
- **THEN** every validation passes
