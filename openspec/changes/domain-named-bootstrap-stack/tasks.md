## 1. Pre-flight

- [ ] 1.1 Retract `rename-taskmanager-to-tjb`: either `git rm -r openspec/changes/rename-taskmanager-to-tjb/` or `git mv` it to `openspec/archived/rename-taskmanager-to-tjb-retracted/` with a `RETRACTED.md` stub explaining that this change supersedes it. Commit with `chore(openspec): retract rename-taskmanager-to-tjb in favor of domain-named-bootstrap-stack`.
- [ ] 1.2 Audit every hardcoded `taskmanager` occurrence in templates and workflow:
  ```powershell
  Get-ChildItem -Path infrastructure -Recurse -File -Include *.template,*.yaml,*.yml |
    Select-String -CaseSensitive -Pattern 'taskmanager' |
    Out-File openspec/changes/domain-named-bootstrap-stack/taskmanager-audit.txt
  Select-String -Path .github/workflows/zbuild.yml -CaseSensitive -Pattern 'taskmanager' |
    Out-File openspec/changes/domain-named-bootstrap-stack/workflow-taskmanager-audit.txt -Append
  ```
- [ ] 1.3 Audit hardcoded references to the current bootstrap stack name (`bootstrap-appcloud-systems`, `bootstrap-` prefix, etc.) in the workflow and any scripts:
  ```powershell
  Select-String -Path .github/workflows/zbuild.yml,scripts/*.ps1,scripts/*.sh -Pattern 'bootstrap-' -CaseSensitive
  ```
- [ ] 1.4 Confirm git working tree is clean and there are no in-flight branches with conflicting template edits. Coordinate with anyone working on `centralize-aurora-kms-keys` (they're at 21/69 tasks).
- [ ] 1.5 Snapshot the production Aurora cluster before any teardown begins:
  ```powershell
  $awscli = "C:\Program Files\Amazon\AWSCLIV2\aws.exe"
  & $awscli rds create-db-cluster-snapshot `
    --db-cluster-identifier <current-prod-cluster-id> `
    --db-cluster-snapshot-identifier prod-pre-domain-rename-$(Get-Date -Format yyyyMMdd) `
    --region us-east-1
  & $awscli rds wait db-cluster-snapshot-available --db-cluster-snapshot-identifier prod-pre-domain-rename-$(Get-Date -Format yyyyMMdd) --region us-east-1
  ```
  Verify the snapshot completes; record its ARN in `openspec/changes/domain-named-bootstrap-stack/prod-snapshot.txt`.
- [ ] 1.6 Schedule maintenance window for the `app` cutover; communicate to stakeholders.

## 2. Source changes — bootstrap.template

- [ ] 2.1 In [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template), replace every `taskmanager-aurora-nonprod` / `taskmanager-aurora-prod` literal in `AWS::KMS::Alias` `AliasName:` properties with `!Sub "alias/${AWS::StackName}-aurora-nonprod"` / `!Sub "alias/${AWS::StackName}-aurora-prod"`.
- [ ] 2.2 In the same template, replace every `/taskmanager/kms/*/aurora-key-arn` SSM `Name:` property with `!Sub "/${AWS::StackName}/kms/nonprod/aurora-key-arn"` / `!Sub "/${AWS::StackName}/kms/prod/aurora-key-arn"`.
- [ ] 2.3 For any other bootstrap-owned resource that embeds `taskmanager` in its name (audited in 1.2 — likely ECR repo name or similar), apply the same `!Sub "${AWS::StackName}-..."` transformation.
- [ ] 2.4 Update the template's `Description:` to read `Bootstrap shared resources. Deploy with stack name = dashed domain (e.g., appcloud-systems for appcloud.systems).` so operators reading the template understand the naming convention.
- [ ] 2.5 Run `aws cloudformation validate-template --template-body file://infrastructure/bootstrap.template --region us-east-1`. Confirm no syntax errors.
- [ ] 2.6 Re-run the audit grep from 1.2 against `bootstrap.template` alone — confirm zero non-comment `taskmanager` matches remain.

## 3. Source changes — env-stack templates

- [ ] 3.1 Add a `DomainName` parameter to [infrastructure/master.template](../../../infrastructure/master.template):
  ```yaml
  DomainName:
    Type: String
    Description: Domain name with dots replaced by dashes (matches the bootstrap stack name)
    AllowedPattern: "^[a-z0-9]+(-[a-z0-9]+)*$"
  ```
  Pass it through to every nested stack invocation that needs it.
- [ ] 3.2 Add the same `DomainName` parameter to [infrastructure/backend.template](../../../infrastructure/backend.template), [infrastructure/db.template](../../../infrastructure/db.template), [infrastructure/web.template](../../../infrastructure/web.template), and any other child template flagged by the 1.2 audit.
- [ ] 3.3 In each env-stack template, replace hardcoded `taskmanager` literals with `!Sub` constructions using `${DomainName}`:
  - Aurora Global Cluster identifier: `!Sub "${DomainName}-${BranchLeaf}-global-cluster"`
  - Aurora regional cluster identifier: `!Sub "${DomainName}-${BranchLeaf}-${AWS::Region}"`
  - Secrets Manager path prefixes: `!Sub "${DomainName}/database/regional/${BranchLeaf}"`
  - IAM policy `Resource:` ARN scopes matching `arn:aws:rds:*:*:cluster:taskmanager-*` → `!Sub "arn:aws:rds:*:*:cluster:${DomainName}-*"`
- [ ] 3.4 In [infrastructure/security.template](../../../infrastructure/security.template), if `SharedLambdaExecutionRole`'s inline policy has any `Resource:` scope matching `taskmanager-*` cluster ARNs, add a `DomainName` parameter to that template and replace the scope with `!Sub "arn:aws:rds:*:*:cluster:${DomainName}-*"`.
- [ ] 3.5 Run `aws cloudformation validate-template` on every changed template. Iterate on syntax errors until all validate clean.
- [ ] 3.6 Re-run the audit grep from 1.2 against `infrastructure/*.template` — confirm zero non-comment `taskmanager` matches remain anywhere in `infrastructure/`.

## 4. Source changes — workflow

- [ ] 4.1 In [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml), add a new step early in each job that computes the dashed-domain value once:
  ```yaml
  - name: Compute domain-dashed
    id: domain
    shell: pwsh
    run: |
      $dashed = '${{ secrets.DOMAIN_NAME }}'.ToLower().Replace('.', '-')
      "dashed=$dashed" >> $env:GITHUB_OUTPUT
  ```
- [ ] 4.2 Find the bootstrap-deploy step (`aws cloudformation deploy --stack-name bootstrap-...`) and change its `--stack-name` argument to `${{ steps.domain.outputs.dashed }}` (no `bootstrap-` prefix).
- [ ] 4.3 Find the "Lookup bootstrap KMS key from SSM" step and change its parameter path from `/taskmanager/kms/${scope}/aurora-key-arn` to `/${{ steps.domain.outputs.dashed }}/kms/${scope}/aurora-key-arn`.
- [ ] 4.4 Find every env-stack deploy step's `--parameter-overrides` argument and append `DomainName=${{ steps.domain.outputs.dashed }}` to it.
- [ ] 4.5 Re-run the workflow grep from 1.2 — confirm zero `taskmanager` and zero hardcoded `appcloud-systems` matches remain in the workflow.

## 5. Update OpenSpec cross-references

- [ ] 5.1 In each in-flight change folder under `openspec/changes/` (excluding `domain-named-bootstrap-stack` itself), grep for `taskmanager` and update narrative prose and example resource names to use the dashed-domain form (e.g., `appcloud-systems`) for illustration. Hand-edit; don't mechanically substitute.
- [ ] 5.2 In [openspec/changes/centralize-aurora-kms-keys/](../centralize-aurora-kms-keys/), update Phase-4 / Phase-5 task examples that reference `alias/taskmanager-aurora-*` or `/taskmanager/kms/*` paths to reference the new derived names. The underlying procedures don't change; only the resource names in the example commands do.
- [ ] 5.3 Run `openspec validate <change>` for every in-flight change to confirm none break from the cross-reference updates.

## 6. Commit and feature-branch test

- [ ] 6.1 Stage the source changes (templates + workflow) and the spec retraction (Task 1.1) into a single commit: `refactor(infrastructure): derive resource naming from bootstrap stack name = dashed domain`. Include a body explaining the destructive cutover that follows.
- [ ] 6.2 Push to a feature branch (NOT to `dev` / `beta` / `alpha` / `app`). The branch's CI will try to deploy an env stack against the OLD bootstrap (which still publishes at `/taskmanager/kms/*`); this is expected to fail at the SSM-lookup step because the workflow now reads from `/appcloud-systems/kms/*`. **That failure is the canary — it confirms the workflow change took effect.**
- [ ] 6.3 Confirm the failure happens at the expected step (SSM lookup) and not earlier. If it fails earlier (template validation, parameter mismatch), fix and re-push.

## 7. Cutover — tear down old stacks

In the maintenance window. Order matters — env stacks must be torn down before bootstrap (some have IAM policies that reference bootstrap-owned KMS keys; bootstrap can't be deleted while anything still references the keys).

- [ ] 7.1 Tear down each env stack in BOTH regions. For each stack, run:
  ```powershell
  $awscli = "C:\Program Files\Amazon\AWSCLIV2\aws.exe"
  & $awscli cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1
  & $awscli cloudformation wait stack-delete-complete --stack-name dev-appcloud-systems --region us-east-1
  ```
  Repeat for: `dev-appcloud-systems`, `beta-appcloud-systems`, `alpha-appcloud-systems`, `app-appcloud-systems` in each of `us-east-1` and `us-west-2`. **If any stack hangs in `DELETE_IN_PROGRESS` with Aurora cluster in `backing-up`, follow the workaround documented in [openspec/changes/robust-aurora-cluster-teardown/](../robust-aurora-cluster-teardown/).**
- [ ] 7.2 Tear down the old bootstrap stack in each region:
  ```powershell
  & $awscli cloudformation delete-stack --stack-name bootstrap-appcloud-systems --region us-east-1
  & $awscli cloudformation wait stack-delete-complete --stack-name bootstrap-appcloud-systems --region us-east-1
  & $awscli cloudformation delete-stack --stack-name bootstrap-appcloud-systems --region us-west-2
  & $awscli cloudformation wait stack-delete-complete --stack-name bootstrap-appcloud-systems --region us-west-2
  ```
- [ ] 7.3 Verify no orphan resources remain that would block recreation:
  ```powershell
  & $awscli kms list-aliases --region us-east-1 --query "Aliases[?starts_with(AliasName, 'alias/taskmanager-')]"
  & $awscli ssm describe-parameters --parameter-filters Key=Name,Option=BeginsWith,Values=/taskmanager --region us-east-1
  & $awscli s3api get-bucket-policy --bucket cf-templates-991795635857-us-east-1 2>&1   # may need to delete
  ```
  Clean any holdouts manually.

## 8. Cutover — deploy new bootstrap and env stacks

- [ ] 8.1 Deploy the new bootstrap stack in `us-east-1` (primary):
  ```powershell
  $awscli = "C:\Program Files\Amazon\AWSCLIV2\aws.exe"
  & $awscli cloudformation deploy `
    --stack-name appcloud-systems `
    --template-file infrastructure/bootstrap.template `
    --parameter-overrides TemplatesBucketName=cf-templates-991795635857-us-east-1 `
    --capabilities CAPABILITY_NAMED_IAM `
    --region us-east-1
  ```
  Verify: `aws kms list-aliases --query "Aliases[?starts_with(AliasName, 'alias/appcloud-systems-')]"` shows two aliases; `aws ssm get-parameter --name /appcloud-systems/kms/nonprod/aurora-key-arn` resolves.
- [ ] 8.2 Capture the primary-region KMS key ARNs from outputs:
  ```powershell
  $primaryNonprodArn = & $awscli cloudformation describe-stacks --stack-name appcloud-systems --region us-east-1 --query "Stacks[0].Outputs[?OutputKey=='AuroraKmsKeyNonprodArn'].OutputValue" --output text
  $primaryProdArn    = & $awscli cloudformation describe-stacks --stack-name appcloud-systems --region us-east-1 --query "Stacks[0].Outputs[?OutputKey=='AuroraKmsKeyProdArn'].OutputValue"    --output text
  ```
- [ ] 8.3 Deploy the new bootstrap stack in `us-west-2` (replica):
  ```powershell
  & $awscli cloudformation deploy `
    --stack-name appcloud-systems `
    --template-file infrastructure/bootstrap.template `
    --parameter-overrides TemplatesBucketName=cf-templates-991795635857-us-west-2 PrimaryNonprodKeyArn=$primaryNonprodArn PrimaryProdKeyArn=$primaryProdArn `
    --capabilities CAPABILITY_NAMED_IAM `
    --region us-west-2
  ```
- [ ] 8.4 Rotate the GitHub Actions AWS access keys in repo secrets to match the keys created by the new bootstrap stack (the access keys are regenerated when the IAM users are recreated):
  - `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` ← `GitHubActionsUser` from new bootstrap outputs
  - `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` ← `GitHubActionsUserProd`
- [ ] 8.5 Trigger a `dev` deploy by pushing a no-op commit. Confirm:
  - Workflow's `Compute domain-dashed` step outputs `appcloud-systems`.
  - SSM lookup at `/appcloud-systems/kms/nonprod/aurora-key-arn` resolves.
  - Env-stack deploy succeeds; new Aurora Global Cluster identifier is `appcloud-systems-dev-global-cluster`.
- [ ] 8.6 Trigger `beta` and `alpha` deploys by pushing no-op commits. Confirm each comes up healthy.
- [ ] 8.7 Trigger the `app` deploy. Restore the prod snapshot from 1.5 into the new prod cluster if data preservation is required (operator decision at execution time):
  ```powershell
  & $awscli rds restore-db-cluster-from-snapshot `
    --db-cluster-identifier appcloud-systems-app-us-east-1 `
    --snapshot-identifier <ARN from 1.5> `
    --engine aurora-mysql `
    --kms-key-id $primaryProdArn `
    --region us-east-1
  ```
  Otherwise, the new `app` cluster starts empty.

## 9. Cleanup and verification

- [ ] 9.1 Re-run the templates audit grep from 1.2 against the entire `infrastructure/` directory. Confirm zero non-comment `taskmanager` matches.
- [ ] 9.2 Re-run the workflow audit grep. Confirm zero `taskmanager` and zero hardcoded `appcloud-systems` matches.
- [ ] 9.3 In both regions, verify no orphan KMS aliases or SSM parameters remain:
  ```powershell
  foreach ($region in @('us-east-1','us-west-2')) {
    & $awscli kms list-aliases --region $region --query "Aliases[?starts_with(AliasName, 'alias/taskmanager-')]"
    & $awscli ssm describe-parameters --parameter-filters Key=Name,Option=BeginsWith,Values=/taskmanager --region $region
  }
  ```
- [ ] 9.4 Schedule the old prod KMS key for deletion if not already pending (the old bootstrap stack's destruction should have done this; verify): `aws kms describe-key --key-id <old-prod-key-arn>` should show `KeyState: PendingDeletion`.
- [ ] 9.5 Spot-check one env-stack's Aurora cluster, Secrets Manager paths, and IAM policies to confirm every previously-`taskmanager` name now reflects the dashed-domain prefix.
- [ ] 9.6 Update [CLAUDE.md](../../../CLAUDE.md)'s "Stack reality vs. README" section to note the new naming convention: bootstrap stack name = dashed domain; resource names derive from `${AWS::StackName}` (bootstrap) or `${DomainName}` parameter (env stacks).

## 10. Validate and archive

- [ ] 10.1 Manually verify each scenario in [specs/domain-derived-resource-naming/spec.md](specs/domain-derived-resource-naming/spec.md):
  - Bootstrap stack name = `appcloud-systems` in both regions.
  - All KMS aliases / SSM parameters / Aurora identifiers / Secrets paths use the dashed-domain prefix.
  - Templates grep returns empty for `taskmanager`.
  - Workflow grep returns empty for `taskmanager` and for hardcoded `appcloud-systems`.
- [ ] 10.2 Manually verify each MODIFIED scenario in [specs/aurora-kms-key-management/spec.md](specs/aurora-kms-key-management/spec.md): aliases, SSM paths, no `bootstrap-` prefix.
- [ ] 10.3 Run `openspec validate domain-named-bootstrap-stack --strict`. Must pass.
- [ ] 10.4 Archive the change per the experimental workflow: `/opsx:archive domain-named-bootstrap-stack`. The capability `domain-derived-resource-naming` is moved to `openspec/specs/`; the delta against `aurora-kms-key-management` is merged into that capability's spec (if it has already been promoted by `centralize-aurora-kms-keys` archiving first; otherwise the merge happens whichever change archives last).
