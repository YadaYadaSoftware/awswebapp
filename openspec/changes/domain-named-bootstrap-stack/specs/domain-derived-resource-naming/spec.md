## ADDED Requirements

### Requirement: Bootstrap stack name equals the deployment domain with dots replaced by dashes

The bootstrap CloudFormation stack ([infrastructure/bootstrap.template](../../../../infrastructure/bootstrap.template)) SHALL be deployed with a stack name equal to the deployment's domain name with every `.` character replaced by `-`. For the `appcloud.systems` deployment, the stack name SHALL be `appcloud-systems`. For a hypothetical `example.com` deployment, the stack name SHALL be `example-com`. The stack name MUST NOT carry any additional prefix (no `bootstrap-`, no `infra-`, etc.).

The same dashed-domain value SHALL serve as the canonical project identifier referenced by every other AWS resource owned by the bootstrap stack and by every env-stack template that needs to reference bootstrap-owned resources.

#### Scenario: Bootstrap stack name in us-east-1
- **WHEN** the bootstrap stack is deployed in `us-east-1` for the `appcloud.systems` domain
- **THEN** `aws cloudformation describe-stacks --stack-name appcloud-systems --region us-east-1` returns a stack in `CREATE_COMPLETE` or `UPDATE_COMPLETE` state, and no stack named `bootstrap-appcloud-systems` (or any name with the `bootstrap-` prefix) exists

#### Scenario: Bootstrap stack name in us-west-2
- **WHEN** the same bootstrap template is deployed in `us-west-2` for the same domain
- **THEN** the stack name in `us-west-2` is also `appcloud-systems` (the dashed-domain naming is region-agnostic)

#### Scenario: Hypothetical second domain
- **WHEN** the same templates are deployed for a hypothetical second domain `example.com` (no source-code edits, only `secrets.DOMAIN_NAME` changed)
- **THEN** the bootstrap stack in each region is named `example-com` and owns resources prefixed `example-com-*`

### Requirement: Bootstrap-owned resources derive names from `${AWS::StackName}`

Every resource defined in [infrastructure/bootstrap.template](../../../../infrastructure/bootstrap.template) whose name previously embedded the literal `taskmanager` SHALL instead construct its name via `!Sub` referencing `${AWS::StackName}`. Specifically:

- KMS alias for nonprod Aurora key: `!Sub "alias/${AWS::StackName}-aurora-nonprod"`
- KMS alias for prod Aurora key: `!Sub "alias/${AWS::StackName}-aurora-prod"`
- SSM parameter for nonprod key ARN: `!Sub "/${AWS::StackName}/kms/nonprod/aurora-key-arn"`
- SSM parameter for prod key ARN: `!Sub "/${AWS::StackName}/kms/prod/aurora-key-arn"`
- Any other bootstrap-owned resource whose name previously contained `taskmanager`

The bootstrap template SHALL contain zero case-sensitive matches for the literal `taskmanager` (validated by grep — see "No `taskmanager` literals survive in templates or workflow" requirement below).

#### Scenario: KMS aliases derive from stack name
- **WHEN** the bootstrap stack named `appcloud-systems` is deployed in `us-east-1`
- **THEN** `aws kms list-aliases --region us-east-1` shows aliases `alias/appcloud-systems-aurora-nonprod` and `alias/appcloud-systems-aurora-prod`, and shows no aliases starting with `alias/taskmanager-`

#### Scenario: SSM paths derive from stack name
- **WHEN** the bootstrap stack named `appcloud-systems` is deployed in `us-east-1`
- **THEN** `aws ssm get-parameter --name /appcloud-systems/kms/nonprod/aurora-key-arn --region us-east-1` returns a valid KMS key ARN, and `aws ssm describe-parameters --parameter-filters Key=Name,Option=BeginsWith,Values=/taskmanager --region us-east-1` returns zero parameters

#### Scenario: Stack-name change propagates to resource names
- **WHEN** the bootstrap stack is destroyed and recreated under a different name (e.g., changing `secrets.DOMAIN_NAME` from `appcloud.systems` to `example.com` for a test deployment)
- **THEN** all KMS aliases and SSM parameters created by the new stack carry the new dashed-domain prefix (`example-com-*` / `/example-com/*`); no manual template edits are needed

### Requirement: Env-stack templates take a `DomainName` parameter

Every env-stack template that previously embedded `taskmanager` or referenced bootstrap-owned resources by hardcoded path SHALL declare a top-level `DomainName` parameter:

```yaml
Parameters:
  DomainName:
    Type: String
    Description: Domain name with dots replaced by dashes (matches the bootstrap stack name)
    AllowedPattern: "^[a-z0-9]+(-[a-z0-9]+)*$"
```

Affected templates SHALL include at minimum:
- [infrastructure/master.template](../../../../infrastructure/master.template)
- [infrastructure/backend.template](../../../../infrastructure/backend.template)
- [infrastructure/db.template](../../../../infrastructure/db.template)
- [infrastructure/web.template](../../../../infrastructure/web.template)
- [infrastructure/security.template](../../../../infrastructure/security.template) (for any inline policy `Resource:` scopes that previously matched `arn:aws:rds:*:*:cluster:taskmanager-*`)
- Any other `infrastructure/*.template` that hardcodes `taskmanager` (audited during Phase 1)

Wherever a template previously used a hardcoded `taskmanager` literal in a resource name, identifier, secret path, or IAM policy `Resource:` scope, it SHALL instead use `!Sub` with `${DomainName}`. For example:

- Aurora Global Cluster identifier: `!Sub "${DomainName}-${BranchLeaf}-global-cluster"`
- Aurora regional cluster identifier: `!Sub "${DomainName}-${BranchLeaf}-${AWS::Region}"`
- Secrets Manager path: `!Sub "${DomainName}/database/regional/${BranchLeaf}"`
- IAM policy `Resource:`: `!Sub "arn:aws:rds:*:*:cluster:${DomainName}-*"`

#### Scenario: master.template declares DomainName parameter
- **WHEN** an operator reads [infrastructure/master.template](../../../../infrastructure/master.template)
- **THEN** a `DomainName` parameter is defined in the `Parameters:` section with the constraint pattern shown above

#### Scenario: Aurora cluster identifier uses DomainName
- **WHEN** the workflow deploys the `dev-appcloud-systems` env stack with `--parameter-overrides DomainName=appcloud-systems`
- **THEN** the resulting Aurora Global Cluster identifier is `appcloud-systems-dev-global-cluster` (no `taskmanager` prefix appears anywhere in the cluster's metadata)

#### Scenario: IAM policy Resource scopes use DomainName
- **WHEN** the workflow deploys any env stack that includes inline IAM policies scoped to Aurora cluster ARNs
- **THEN** every `Resource:` ARN in those policies expands to `arn:aws:rds:*:*:cluster:appcloud-systems-*` (no `taskmanager-*` patterns appear)

### Requirement: Workflow computes the dashed-domain value once and passes it to every deploy

The GitHub Actions deploy workflow ([.github/workflows/zbuild.yml](../../../../.github/workflows/zbuild.yml)) SHALL compute the dashed-domain value once per job from `secrets.DOMAIN_NAME` (replace `.` with `-`, lowercase) and expose it as a step output. Every subsequent step that uses this value — bootstrap-deploy `--stack-name`, SSM-lookup parameter path, env-stack `--parameter-overrides DomainName=...` — SHALL reference the same step output.

The workflow SHALL NOT contain any hardcoded reference to `taskmanager` or to any specific dashed-domain value (such as `appcloud-systems` written as a literal).

#### Scenario: Single source of truth for dashed domain
- **WHEN** an operator reads the deploy workflow
- **THEN** exactly one step computes the dashed-domain value; all downstream references use that step's output via `${{ steps.<id>.outputs.dashed }}`

#### Scenario: Bootstrap deploy uses dashed domain as stack name
- **WHEN** the bootstrap-deploy step runs for the `appcloud.systems` deployment
- **THEN** the `aws cloudformation deploy` invocation uses `--stack-name appcloud-systems` (the dashed-domain value verbatim, no prefix)

#### Scenario: SSM lookup uses dashed domain in path
- **WHEN** the "Lookup bootstrap KMS key from SSM" step runs for any branch
- **THEN** it reads from `/${dashed}/kms/${scope}/aurora-key-arn` (where `${dashed}` is the workflow-computed value and `${scope}` is `prod` for `app` branch / `nonprod` otherwise)

#### Scenario: Env stack deploy passes DomainName parameter
- **WHEN** any env stack is deployed by the workflow
- **THEN** the `aws cloudformation deploy` invocation includes `--parameter-overrides ... DomainName=${dashed} ...` (along with any other parameters)

### Requirement: No `taskmanager` literals survive in templates or workflow

After this change is applied, the repository's CloudFormation templates and deploy workflow SHALL contain zero case-sensitive matches for the literal `taskmanager`. This is verified by grep:

```powershell
Get-ChildItem -Path infrastructure -Recurse -File -Include *.template,*.yaml,*.yml |
  Select-String -CaseSensitive -Pattern 'taskmanager' |
  Where-Object { $_.Line -notmatch '^\s*#' }
Select-String -Path .github/workflows/zbuild.yml -CaseSensitive -Pattern 'taskmanager'
```

Both queries MUST return empty. Comment lines (starting with `#`) are excluded from the templates grep because they may legitimately mention the old naming in historical context; the workflow grep has no exclusion (the workflow YAML doesn't carry historical commentary).

Code-level references (`Tjb.*` .NET namespaces, `tjb` in `.cs` / `.csproj` filenames) are **out of scope** for this requirement — this change does not touch application code.

#### Scenario: Templates grep returns empty
- **WHEN** the templates grep above is run after this change is applied
- **THEN** zero non-comment matches are reported

#### Scenario: Workflow grep returns empty
- **WHEN** the workflow grep above is run after this change is applied
- **THEN** zero matches are reported

#### Scenario: Adding a new template that hardcodes `taskmanager` breaks the build
- **WHEN** a developer adds a new `infrastructure/*.template` file containing the literal `taskmanager`
- **THEN** a CI check (added as part of this change) runs the same grep and fails the build with a message pointing at the offending file and line
