## Why

The bootstrap stack and all the AWS resources it owns are named with a hardcoded `taskmanager` literal (`alias/taskmanager-aurora-nonprod`, `/taskmanager/kms/prod/aurora-key-arn`, etc.), and the bootstrap stack itself carries an arbitrary `bootstrap-` prefix on top of the domain (`bootstrap-appcloud-systems`). Both choices bake project-specific assumptions into infrastructure that is otherwise generic. If this same infrastructure is ever deployed for a second domain — or if the project is renamed — every hardcoded literal needs to be edited.

The fix is to derive resource names from the bootstrap stack's own name and make the bootstrap stack name equal to the domain (with `.` → `-`). Then the same templates work for any domain without modification, and there is no "tjb" / "taskmanager" / project-name decision to make.

## What Changes

- **BREAKING — destructive teardown of all stacks and recreation:** the bootstrap stack is renamed and all resources it owns are renamed; env stacks consume new SSM paths. The existing `bootstrap-appcloud-systems`, `dev-appcloud-systems`, `beta-appcloud-systems`, `alpha-appcloud-systems`, and `app-appcloud-systems` stacks (in both regions) are deleted and recreated under the new naming convention. No in-place migration is attempted.
- **Bootstrap stack name = domain (with `.` → `-`):** `bootstrap-appcloud-systems` → `appcloud-systems`. For a hypothetical `example.com` deployment, the bootstrap stack would simply be `example-com`. The `bootstrap-` prefix is dropped entirely.
- **Env stack names unchanged in shape:** `{branch}-{domain}` is preserved — `app-appcloud-systems`, `dev-appcloud-systems`, etc.
- **All hardcoded `taskmanager-*` / `/taskmanager/*` literals in `bootstrap.template` replaced with `${AWS::StackName}`-derived `!Sub` references:**
  - KMS aliases: `alias/taskmanager-aurora-{prod,nonprod}` → `alias/${AWS::StackName}-aurora-{prod,nonprod}` (resolves to `alias/appcloud-systems-aurora-{prod,nonprod}` for our deployment).
  - SSM Parameter Store paths: `/taskmanager/kms/{prod,nonprod}/aurora-key-arn` → `/${AWS::StackName}/kms/{prod,nonprod}/aurora-key-arn` (resolves to `/appcloud-systems/kms/{prod,nonprod}/aurora-key-arn`).
  - Any other bootstrap-owned resource that currently embeds `taskmanager` follows the same pattern.
- **`DomainName` parameter added to env-stack templates** (`master.template`, `backend.template`, plus any child template that constructs the SSM path or references KMS aliases). The workflow passes `DomainName=appcloud-systems` (derived from `secrets.DOMAIN_NAME` with `.` → `-`, exactly as it already computes for the env-stack name). Templates use `!Sub "/${DomainName}/kms/${Scope}/aurora-key-arn"` to construct the lookup path.
- **Workflow update:** `.github/workflows/zbuild.yml` adds a step that computes `DomainNameDashed` from `secrets.DOMAIN_NAME` (replace `.` → `-`), passes it as `DomainName` parameter override to every CFN deploy, and reads bootstrap SSM params from the new path (`/<domain-dashed>/kms/<scope>/aurora-key-arn`) instead of the hardcoded `/taskmanager/kms/*`.
- **Aurora identifiers also follow the domain convention** where they were previously `taskmanager-*` (Global Cluster identifier, regional cluster identifiers, Secrets Manager paths, IAM policy `Resource:` scopes that match cluster ARN patterns). These derive from `DomainName` (env stacks know it as a parameter).
- **Supersedes `rename-taskmanager-to-tjb`:** that change's premise — pick a single hardcoded project name (`tjb`) and substitute it for `taskmanager` — is replaced. `tjb` stays in code-level namespaces (`Tjb.Web`, `Tjb.Api`, `Tjb.Migrations`, etc. — those are .NET assembly names, not deployable AWS resources) but every AWS resource name derives from the bootstrap stack name = domain. The `rename-taskmanager-to-tjb` change is already committed to the repo as a proposal but should be archived or explicitly retracted before this one lands.

## Capabilities

### New Capabilities

- `domain-derived-resource-naming`: Establishes the invariant that AWS resource names owned by the bootstrap stack derive from `${AWS::StackName}` (which equals the domain with `.` → `-`), and that env-stack resources derive from a `DomainName` CFN parameter passed in by the workflow. No hardcoded project literals appear in any deployable CloudFormation template.

### Modified Capabilities

- `aurora-kms-key-management` (owned by `centralize-aurora-kms-keys`): the scenarios that pin KMS alias names to `alias/taskmanager-aurora-{prod,nonprod}` and SSM paths to `/taskmanager/kms/{prod,nonprod}/aurora-key-arn` change to describe the `${AWS::StackName}`-derived form. The underlying behavior (two keys, prod isolation, SSM publication) is unchanged.

## Impact

**Stack renames (both regions, us-east-1 and us-west-2):**
- `bootstrap-appcloud-systems` → `appcloud-systems` (delete + recreate)
- `dev-appcloud-systems`, `beta-appcloud-systems`, `alpha-appcloud-systems`, `app-appcloud-systems` — kept as-is structurally, but deleted + recreated because the resources they own (Aurora clusters, secrets, role inline-policy scopes) change identifiers/paths.

**AWS resource renames (all derived, not hardcoded):**
- KMS aliases: `alias/taskmanager-aurora-{prod,nonprod}` → `alias/appcloud-systems-aurora-{prod,nonprod}`. Underlying key ARNs change too (new keys created by the new bootstrap stack); old keys go into 30-day pending deletion after the old bootstrap is destroyed.
- SSM Parameter Store: `/taskmanager/kms/*` paths deleted with the old bootstrap stack; new paths under `/appcloud-systems/kms/*` published by the new bootstrap stack.
- Aurora identifiers: previously hardcoded `taskmanager-*` patterns become `${DomainName}-*` patterns in env-stack templates.
- ECR repo / other bootstrap-owned resources: same pattern.

**Files touched:**
- `infrastructure/bootstrap.template` — replace every `taskmanager` literal with `!Sub "${AWS::StackName}"` constructions.
- `infrastructure/master.template`, `backend.template`, `db.template`, `web.template`, etc. — add `DomainName` parameter; replace hardcoded literals with `!Sub` constructions using `${DomainName}`.
- `.github/workflows/zbuild.yml` — compute `DomainNameDashed`; pass as parameter override on every `aws cloudformation deploy`; update SSM-lookup step's path.
- `openspec/specs/aurora-kms-key-management/spec.md` (and the corresponding delta in `centralize-aurora-kms-keys/specs/`) — update KMS alias / SSM path scenarios to reference the `${AWS::StackName}`-derived form.
- Any other `infrastructure/*.template` that hardcodes `taskmanager` (audited during Phase 1).

**Stakeholder impact:**
- Operator manually deletes all stacks (both regions, all branches) before the new bootstrap can be created — there is no automatic cutover. A maintenance window is required for `app`.
- All existing Aurora data is lost unless explicitly snapshotted first. The change assumes nonprod data is disposable; prod is snapshotted in Phase 4 of `tasks.md`.
- After this change lands, the templates are domain-agnostic — deploying the same code for a second domain requires only changing `secrets.DOMAIN_NAME`.

**Out of scope (explicitly):**
- Renaming the GitHub repository, organization, or branch model (`app`/`beta`/`alpha`/`dev`).
- Renaming .NET assemblies (`Tjb.Web`, `Tjb.Api`, etc.) — those are code-level namespaces, not AWS resources.
- Renaming IAM users (`GitHubActionsUser`, `GitHubActionsUserProd`) — generic names that don't embed the project.
- Renaming the `cf-templates-{account}-{region}` S3 bucket — externally managed.
- Building a multi-domain deploy pipeline. This change makes the templates ready for it; the workflow still deploys exactly one domain (the `secrets.DOMAIN_NAME` value).
