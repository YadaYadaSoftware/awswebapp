## 1. Pre-flight audit

- [ ] 1.1 Run the audit grep documented in design.md §D7 and save the file list to `openspec/changes/rename-taskmanager-to-tjb/rename-audit.txt`. Expected: ~51 files across `infrastructure/`, `openspec/`, `.github/`, `src/`, root-level `*.md`.
- [ ] 1.2 Scan for **file names** containing `TaskManager` / `taskmanager`: `Get-ChildItem -Path . -Recurse -File | Where-Object { $_.Name -cmatch '(TaskManager|Taskmanager|taskmanager)' }`. Save to `rename-file-audit.txt`. These need `git mv` rather than text replacement.
- [ ] 1.3 Confirm no developer has uncommitted work in flight that would conflict with a mechanical rename pass (`git status` clean on dev).
- [ ] 1.4 Choose deploy quiet window for Phase 3+ (bootstrap update + env-stack recreate). Communicate to stakeholders that `app` will need a maintenance window.

## 2. Phase 1 — Source-tree rename

- [ ] 2.1 Run the case-aware text replacement from design.md §D1 across all eligible globs. Use the PowerShell script that processes `Task Manager`, `Taskmanager`, `TaskManager`, then `taskmanager` in that order, with `-creplace` to enforce case-sensitivity, and skips `obj/`, `bin/`, `node_modules/`, `.git/`.
- [ ] 2.2 `git mv` any files identified in 1.2. Common candidates: `scripts/Connect-TaskManagerDB.ps1` → `scripts/Connect-TjbDB.ps1` (if it exists; the project's CLAUDE.md references this filename).
- [ ] 2.3 Review the `git diff` for each touched OpenSpec doc — narrative prose that mentions the old name may read awkwardly with mechanical substitution. Hand-edit any rough sentences to use "the project" or "Tjb's …" rather than just "Tjb …" where the original sentence flow needed a multi-word noun. Files to spot-check: `centralize-aurora-kms-keys/proposal.md`, `centralize-aurora-kms-keys/design.md`, `make-deployment-stack-reusable/design.md` (which mentions "taskmanager" extensively as the example).
- [ ] 2.4 Run `dotnet build` to verify the `.cs` / `.csproj` changes compile and the regenerated `obj/**/AssemblyInfo.cs` files now carry the new names.
- [ ] 2.5 Run `aws cloudformation validate-template` on each touched template in `infrastructure/*.template` (use the existing PowerShell loop pattern).
- [ ] 2.6 Run `openspec validate <change> --strict` for each change in `openspec/changes/` (including this one).
- [ ] 2.7 Run the validation grep from design.md §D7 — expect empty output. Any remaining hits → fix and re-run.

## 3. Phase 2 — Commit and push (no AWS impact yet)

- [ ] 3.1 Stage everything and create a single commit with message `refactor: rename TaskManager/taskmanager to Tjb/tjb across the repo`. Include the conventional-commit body explaining the file-types touched and the AWS cutover that follows.
- [ ] 3.2 Push to dev. CI fires on dev; the workflow's existing SSM-lookup step still targets the *old* path (`/taskmanager/kms/*`) because bootstrap hasn't been redeployed yet — that's expected; the lookup still works because the old SSM parameter still exists. The dev env-stack update should be a no-op or minimal (only CFN template `Description:` strings changed).
- [ ] 3.3 Confirm dev CI is green. No env-stack-level breakage is expected at this phase.

## 4. Phase 3 — Bootstrap redeploy (AWS cutover begins)

- [ ] 4.1 In the chosen quiet window, operator runs:
  ```powershell
  & "C:\Program Files\Amazon\AWSCLIV2\aws.exe" cloudformation deploy `
    --stack-name bootstrap-appcloud-systems `
    --template-file infrastructure/bootstrap.template `
    --parameter-overrides TemplatesBucketName=cf-templates-991795635857-us-east-1 `
    --capabilities CAPABILITY_NAMED_IAM `
    --region us-east-1
  ```
  CloudFormation renames the KMS aliases (`alias/taskmanager-aurora-*` → `alias/tjb-aurora-*`) and creates new SSM parameters at `/tjb/kms/*`. The underlying KMS key ARNs do NOT change. The old SSM parameters at `/taskmanager/kms/*` remain (CFN doesn't auto-delete them since they're considered separate resources with different `Name`).
- [ ] 4.2 Verify in us-east-1: `aws ssm get-parameter --name /tjb/kms/nonprod/aurora-key-arn --region us-east-1` returns the same ARN that `/taskmanager/kms/nonprod/aurora-key-arn` does (both resolve, both point at the same key). `aws kms list-aliases --region us-east-1 --query "Aliases[?starts_with(AliasName, 'alias/tjb-')]"` shows the two new aliases.
- [ ] 4.3 Repeat 4.1 for us-west-2 with the appropriate `PrimaryNonprodKeyArn` / `PrimaryProdKeyArn` / `TemplatesBucketName` overrides.
- [ ] 4.4 (Optional) push a feature branch to verify CI uses the new SSM path. The "Lookup bootstrap KMS key from SSM" step's path was updated in Phase 1 to `/tjb/kms/*`, so this confirms end-to-end workflow ↔ AWS plumbing.

## 5. Phase 4 — Env-stack cluster recreates

These steps follow the pattern already documented in [centralize-aurora-kms-keys/tasks.md](../centralize-aurora-kms-keys/tasks.md) Phase 3-4. The trigger is different (rename rather than KMS migration) but the procedure is identical: cluster identifiers are immutable, so the env-stack delete-and-recreate flow applies.

- [ ] 5.1 dev: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`, wait for `DELETE_COMPLETE`, trigger CI redeploy. Verify new cluster identifier is `tjb-dev-global-cluster`.
- [ ] 5.2 beta: same pattern. Snapshot first if you care about preserving data.
- [ ] 5.3 alpha: same pattern.
- [ ] 5.4 app: scheduled maintenance window; parallel-cluster cutover per the existing prod migration procedure.
- [ ] 5.5 After each env stack: verify the new Aurora cluster identifier carries the `tjb-` prefix; verify Secrets Manager paths follow the new convention (`tjb/database/regional/*`).

## 6. Phase 5 — Cleanup

- [ ] 6.1 Delete orphaned old SSM parameters in both regions:
  ```powershell
  $awscli = "C:\Program Files\Amazon\AWSCLIV2\aws.exe"
  foreach ($region in @('us-east-1','us-west-2')) {
    foreach ($name in @('/taskmanager/kms/nonprod/aurora-key-arn','/taskmanager/kms/prod/aurora-key-arn')) {
      & $awscli ssm delete-parameter --name $name --region $region 2>&1
    }
  }
  ```
- [ ] 6.2 Search for any other orphaned `taskmanager`-named AWS resources (KMS aliases that didn't get auto-renamed, Secrets Manager secrets for env stacks that already migrated, etc.): `aws kms list-aliases --query "Aliases[?starts_with(AliasName, 'alias/taskmanager-')]"`, `aws secretsmanager list-secrets --query "SecretList[?starts_with(Name, 'taskmanager/')]"`. Clean each.
- [ ] 6.3 Verify the validation grep from design.md §D7 still returns empty across the whole repo (in case a follow-on commit reintroduced a `TaskManager` reference somewhere — paranoia step).
- [ ] 6.4 `openspec validate rename-taskmanager-to-tjb --strict` — passes.

## 7. Validation + archive

- [ ] 7.1 Manually verify each requirement scenario from `specs/project-naming-convention/spec.md`:
  - Grep audit empty.
  - KMS aliases use tjb prefix; legacy aliases gone (or in `PendingDeletion`).
  - SSM paths use tjb namespace; legacy paths deleted.
  - Aurora cluster identifiers use tjb prefix.
  - All file names use tjb (none use the old name).
  - All in-flight OpenSpec changes still `openspec validate --strict` cleanly.
- [ ] 7.2 Archive this change per the experimental workflow (`/opsx:archive`).
