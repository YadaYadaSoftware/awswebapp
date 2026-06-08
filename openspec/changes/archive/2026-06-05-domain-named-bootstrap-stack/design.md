## Context

Today the bootstrap stack is named `bootstrap-appcloud-systems` (us-east-1) / `bootstrap-appcloud-systems` (us-west-2) and owns AWS resources whose names are hardcoded around the literal `taskmanager`:

| Resource | Current name | Defined in |
|---|---|---|
| KMS alias (nonprod) | `alias/taskmanager-aurora-nonprod` | `infrastructure/bootstrap.template` |
| KMS alias (prod) | `alias/taskmanager-aurora-prod` | `infrastructure/bootstrap.template` |
| SSM param (nonprod key ARN) | `/taskmanager/kms/nonprod/aurora-key-arn` | `infrastructure/bootstrap.template` |
| SSM param (prod key ARN) | `/taskmanager/kms/prod/aurora-key-arn` | `infrastructure/bootstrap.template` |
| Aurora Global Cluster identifier | `taskmanager-{branch}-global-cluster` | env-stack templates (`db.template` / `backend.template`) |
| Aurora regional cluster identifier | `taskmanager-{branch}-{region}` | env-stack templates |
| Secrets Manager paths | `taskmanager/database/regional/*` | env-stack templates |
| IAM `Resource:` scopes on cluster ARNs | `arn:aws:rds:*:*:cluster:taskmanager-*` | `SharedLambdaExecutionRole`, deployment policies |
| ECR repo (if named with project prefix) | TBD during Phase 1 audit | `infrastructure/bootstrap.template` |

The workflow (`.github/workflows/zbuild.yml`) has a "Lookup bootstrap KMS key from SSM" step that hardcodes the `/taskmanager/kms/*` path. The bootstrap stack name itself is set in the workflow's bootstrap-deploy step (currently `bootstrap-appcloud-systems`).

There is a separate already-committed OpenSpec change `rename-taskmanager-to-tjb` that proposes a different solution: substitute every `taskmanager` literal with a `tjb` literal. That change is now superseded by this one — the new direction is to derive the prefix from `${AWS::StackName}` rather than swap one hardcoded value for another. The user has already committed the `rename-taskmanager-to-tjb` proposal but hasn't started applying it; this change must explicitly retract/archive it before landing.

The user has authorized a destructive teardown: every existing stack in both regions (`bootstrap-appcloud-systems`, `dev-appcloud-systems`, `beta-appcloud-systems`, `alpha-appcloud-systems`, `app-appcloud-systems`) is deleted and recreated. There is no in-place migration. Data preservation is opt-in via snapshot (prod only).

## Goals / Non-Goals

**Goals:**
- Bootstrap stack name = domain (with `.` → `-`). For our deployment: `appcloud-systems`. For a hypothetical `example.com` deployment: `example-com`.
- Every AWS resource name owned by the bootstrap stack derives from `${AWS::StackName}` via `!Sub`. Zero `taskmanager` (or any other project literal) appears in `bootstrap.template`.
- Env-stack templates (`master.template`, `backend.template`, `db.template`, `web.template`, plus inline policies in `security.template`) take a `DomainName` parameter (set by the workflow to the dashed-domain form) and derive every formerly hardcoded resource name from it.
- The deploy workflow computes `DomainNameDashed` once, passes it as a parameter override to every CloudFormation deploy (bootstrap stack via `--stack-name`, env stack via both `--stack-name` and `--parameter-overrides DomainName=...`), and uses it to construct the SSM lookup path for the bootstrap KMS key.
- Same templates can be deployed for any future domain without source edits — only `secrets.DOMAIN_NAME` changes.
- The `rename-taskmanager-to-tjb` change is explicitly retracted (archived without implementation) before this change lands.

**Non-Goals:**
- Implementing multi-domain deploys. The pipeline still deploys exactly one domain (the `secrets.DOMAIN_NAME` value); this change only makes the templates ready for it.
- Renaming .NET assemblies, namespaces, or any code-level identifier. `Tjb.Web`, `Tjb.Api`, `Tjb.Migrations`, etc. remain.
- Preserving existing Aurora data on nonprod env stacks. Those are torn down and recreated empty.
- Backwards compatibility with the old `/taskmanager/*` SSM paths. The workflow stops reading them; the old paths are deleted with the old bootstrap stack.
- Renaming IAM users, the S3 templates bucket, GitHub Actions secrets, or the branch model (`app`/`beta`/`alpha`/`dev`).
- Changing how the secondary region is named (still `us-west-2`; the in-flight `shift-secondary-region-to-us-east-2` change is orthogonal).

## Decisions

### D1. Bootstrap stack name = bare domain (no `bootstrap-` prefix)

Stack name = `secrets.DOMAIN_NAME` with `.` → `-`. For `appcloud.systems`, the stack is `appcloud-systems`. For `example.com`, it would be `example-com`.

**Why**: It removes one degree of arbitrary naming. The domain *is* the project identifier. There is exactly one bootstrap stack per region per domain, so there's no ambiguity about what "the bootstrap stack" refers to. CFN intrinsic `${AWS::StackName}` then yields the correct project prefix for every resource the bootstrap owns.

**Alternative considered**: Keep `bootstrap-` prefix and use `!Select [1, !Split ["bootstrap-", !Ref "AWS::StackName"]]` to extract the suffix.
- **Why rejected**: Adds template complexity for no benefit. The bootstrap stack doesn't need a "bootstrap-" prefix to be recognizable — its IAM users, KMS keys, and SSM paths are diagnostic enough. Operators can tell what each stack is by listing them (the bootstrap stack has no branch prefix; env stacks always do).

**Alternative considered**: Use stack tags (`Type=bootstrap`) to identify the bootstrap stack without requiring a naming convention.
- **Why rejected**: Tags aren't visible in `aws cloudformation list-stacks` output by default, and they can't be queried by CFN intrinsics. Naming carries the convention more reliably.

### D2. Bootstrap-owned resources derive names from `${AWS::StackName}`

Every resource defined in `bootstrap.template` that previously used `taskmanager` in its name now uses `!Sub "${AWS::StackName}-..."` or `!Sub "/${AWS::StackName}/..."`:

```yaml
NonprodAuroraKmsAlias:
  Type: AWS::KMS::Alias
  Properties:
    AliasName: !Sub "alias/${AWS::StackName}-aurora-nonprod"
    TargetKeyId: !Ref NonprodAuroraKmsKey

NonprodAuroraKeyArnParam:
  Type: AWS::SSM::Parameter
  Properties:
    Name: !Sub "/${AWS::StackName}/kms/nonprod/aurora-key-arn"
    Type: String
    Value: !GetAtt NonprodAuroraKmsKey.Arn
```

**Why**: `${AWS::StackName}` is always defined and always equals the actual deployed stack name. No parameter required. No risk of drift between the stack name and the resource names. If the stack is renamed via redeploy under a new name (which requires teardown anyway since most resource names are immutable), the resources rename atomically.

**Risk — KMS alias maximum length**: KMS alias names are limited to 256 chars (after the `alias/` prefix). `appcloud-systems-aurora-nonprod` is well under that. A hypothetically very long domain (>200 chars without dots) would exceed the limit; this is a non-issue in practice but worth noting.

### D3. Env stacks take `DomainName` as a CFN parameter

Env-stack templates (`master.template`, `backend.template`, plus children) get a new top-level parameter:

```yaml
Parameters:
  DomainName:
    Type: String
    Description: Domain name with dots replaced by dashes (e.g., 'appcloud-systems'). Used to build SSM lookup paths and to prefix Aurora cluster identifiers, matching the bootstrap stack name.
    AllowedPattern: "^[a-z0-9]+(-[a-z0-9]+)*$"
    ConstraintDescription: must be lowercase letters/digits separated by single dashes (the dashed form of a domain)
```

The workflow computes this once from `secrets.DOMAIN_NAME` and passes it as `--parameter-overrides DomainName=$DomainNameDashed` on every env-stack deploy. Templates construct SSM paths as `!Sub "/${DomainName}/kms/${Scope}/aurora-key-arn"` and Aurora cluster identifiers as `!Sub "${DomainName}-${Branch}-global-cluster"`.

**Why pass as parameter instead of derive from env-stack name**: Env stack names follow `{branch-leaf}-{domain-dashed}`. CFN's `Fn::Split` has no "split once" mode — for a multi-segment branch like `feature-xyz-appcloud-systems`, splitting on `-` yields four tokens and there is no reliable way to rejoin all but the first. Passing the parameter explicitly is simpler, more robust, and matches what the workflow already knows.

**Alternative considered**: Use `Fn::Join` + `Fn::Split` gymnastics with a known list of branch prefixes.
- **Why rejected**: Brittle. Adding a new branch prefix would require template edits. Doesn't handle feature branches at all.

**Alternative considered**: Have the workflow do the SSM lookup itself (read the KMS key ARN) and pass that ARN as a parameter to env stacks. Env stacks never need to know the domain.
- **Why partially adopted**: This is what the workflow already does for the KMS key ARN (passing `KmsKeyArn`). Continue that pattern for the KMS key ARN specifically. But env stacks still need `DomainName` for: Aurora cluster identifiers, Secrets Manager path prefixes, IAM policy `Resource:` scopes. So `DomainName` parameter is still required.

### D4. Workflow computes `DomainNameDashed` once and reuses it

In `.github/workflows/zbuild.yml`, add a step early in each job (after the existing domain processing for env-stack name computation, which is conceptually the same operation):

```yaml
- name: Compute domain-dashed
  id: domain
  shell: pwsh
  run: |
    $dashed = '${{ secrets.DOMAIN_NAME }}'.Replace('.', '-')
    "dashed=$dashed" >> $env:GITHUB_OUTPUT
```

Reuse `${{ steps.domain.outputs.dashed }}` in:
- Bootstrap-deploy step's `--stack-name` (= the dashed value, no prefix).
- The "Lookup bootstrap KMS key from SSM" step's parameter path: `/${{ steps.domain.outputs.dashed }}/kms/${Scope}/aurora-key-arn`.
- Env-stack deploy step's `--parameter-overrides DomainName=${{ steps.domain.outputs.dashed }}`.

**Why a single computed value**: Single source of truth. The workflow already has logic for the env-stack name suffix (which is the same dashed-domain string); centralizing it removes duplication and the risk of two slightly-different transformations.

### D5. KMS key ARNs flow as today (workflow-resolved, passed as parameter)

The workflow continues to read the appropriate KMS key ARN from SSM (`/${dashed}/kms/${scope}/aurora-key-arn`) and pass it as `--parameter-overrides KmsKeyArn=...` to the env stack. Env-stack templates take a `KmsKeyArn` parameter as today; they do NOT construct SSM paths themselves.

**Why**: Two reasons. First, branch-conditional scope (`prod` vs `nonprod`) lives in the workflow already — re-encoding it in the template is duplication. Second, dynamic SSM references in CFN have well-known pitfalls (caching of `dynamic-references` values during stack updates, cross-region restrictions). The workflow-side lookup avoids both.

### D6. Two-step deploy ordering preserved

The existing two-step pattern stays: (1) deploy/update bootstrap stack first if needed; (2) workflow reads `/${dashed}/kms/${scope}/aurora-key-arn`; (3) deploy env stack with `KmsKeyArn` + new `DomainName` parameter. Nothing about the ordering changes — only the names of the stacks and the SSM paths.

### D7. Audit grep — derivation completeness

The acceptance criteria includes a grep that proves no `taskmanager` literal survives in any CFN template or in the workflow:

```powershell
Get-ChildItem -Path infrastructure -Recurse -File -Include *.template,*.yaml,*.yml |
  Select-String -CaseSensitive -Pattern 'taskmanager' |
  Where-Object { $_.Line -notmatch '^\s*#' }   # ignore comment lines
```

Plus for the workflow:

```powershell
Select-String -Path .github/workflows/zbuild.yml -CaseSensitive -Pattern 'taskmanager'
```

Both must return empty. Code-level `Tjb` namespace references are out of scope (this change does not modify .NET assemblies).

### D8. Retract `rename-taskmanager-to-tjb` before this lands

Because both changes touch the same files and their requirement sets are incompatible (one says "rename to `tjb`", the other says "derive from stack name"), `rename-taskmanager-to-tjb` must be removed before this one is applied:

- Archive its directory: `git mv openspec/changes/rename-taskmanager-to-tjb openspec/archived/rename-taskmanager-to-tjb-retracted/` and add a `RETRACTED.md` explaining the supersession.
- Or delete it: `git rm -r openspec/changes/rename-taskmanager-to-tjb/` if the team prefers a clean removal.

Choice deferred to the operator at retraction time. Tasks.md documents both options.

## Risks / Trade-offs

- **[Risk] Destructive teardown of `app` stack means a production outage window.** → Mitigation: Phase 4 of tasks.md requires snapshotting the prod Aurora cluster first, then a documented maintenance-window cutover (same pattern as `centralize-aurora-kms-keys/tasks.md` Phase 4). All nonprod stacks are torn down without snapshot.
- **[Risk] Old KMS keys (with `taskmanager`-prefixed aliases) go into 30-day pending deletion when the old bootstrap is destroyed; if anything still references them by ARN, that breaks.** → Mitigation: The cleanup grep in Phase 5 explicitly looks for any remaining ARN references to the old keys. Env stacks always look up the KMS key by SSM path at deploy time, so once they redeploy under the new bootstrap, they reference the new keys.
- **[Risk] Old SSM parameters at `/taskmanager/*` won't be deleted automatically (they belong to the old bootstrap stack — destroying it removes them; but if any other stack created similarly-named params, they would orphan).** → Mitigation: Phase 5 includes a grep / `aws ssm describe-parameters --parameter-filters Key=Name,Option=BeginsWith,Values=/taskmanager` to verify zero remain in each region.
- **[Risk] The `DomainName` parameter is new; if anyone deploys an env stack with the old workflow YAML cached (e.g., from a long-running PR branch), the template will fail to find `DomainName` and reject the parameter.** → Mitigation: Land the workflow change in the same PR as the template change. Re-run CI on any open PR branches after merge.
- **[Risk] Operator typos a stack name as `appcloud_systems` (underscore) or `appcloud.systems` (dot), violating the assumption that `${AWS::StackName}` is a valid alias/SSM-path component.** → Mitigation: Bootstrap deploy command in `tasks.md` is documented with the exact stack name; the workflow computes it programmatically so manual deploys are the only risk path. KMS alias names disallow `.`; CFN would fail to create the alias and surface the error.
- **[Trade-off] `${AWS::StackName}` is opaque inside the template — reading `bootstrap.template` doesn't tell you what the alias will be named.** → Accepted: this is the price of being domain-agnostic. The README and CLAUDE.md will explain that the bootstrap stack name = dashed domain = the project prefix.
- **[Trade-off] An operator manually deploying bootstrap with a different stack name would create resources with that name, possibly silently diverging from what the workflow does.** → Accepted: only one person/automation deploys bootstrap; the workflow documents the exact command. Manual deploys for testing should use the same stack name.

## Migration Plan

See `tasks.md` for the step-by-step procedure. High-level shape:

1. **Pre-flight**: snapshot prod Aurora, audit which `taskmanager` literals exist, retract `rename-taskmanager-to-tjb`.
2. **Source changes**: edit `bootstrap.template`, env-stack templates, workflow YAML. Commit. Push to a feature branch (NOT dev/app — those would try to deploy immediately).
3. **Validate**: `aws cloudformation validate-template` on every changed template; `openspec validate domain-named-bootstrap-stack --strict`.
4. **Cutover**: in maintenance window, operator manually tears down all stacks in both regions, then triggers the new bootstrap deploy via the feature branch (or directly via AWS CLI), then merges to dev to trigger env-stack recreates per branch.
5. **Cleanup**: delete orphan SSM params at `/taskmanager/*`; verify grep returns empty; archive this change.

No rollback path other than redeploying the old templates from git history. Rollback would itself be destructive (re-teardown). The change is one-way.

## Open Questions

- **Q1**: Should the bootstrap stack name also be lowercased explicitly? `secrets.DOMAIN_NAME` is conventionally lowercase but not validated. → Tentative answer: enforce lowercase in the workflow step (`.ToLower()`) and add an `AllowedPattern` regex on `DomainName` in the env-stack templates.
- **Q2**: For ECR repo and any other bootstrap-owned resources currently named with `taskmanager`, do any have naming constraints that the dashed-domain might violate? → Tentative answer: Phase 1 audit identifies all of them; `appcloud-systems` is conservative (lowercase alphanumeric + dash) and meets every AWS naming rule for the resources we touch. Confirm during audit.
- **Q3**: Should the prod Aurora snapshot be restored into the new cluster, or is a fresh empty cluster acceptable? → Defer to operator at execution time; tasks.md flags this as a checkpoint decision.
