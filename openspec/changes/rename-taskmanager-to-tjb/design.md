## Context

A naming-history audit of the repo (via `grep` across `infrastructure/`, `openspec/`, `.github/`, `src/`, `scripts/`, root-level `.md`) returns matches in 51+ files for the strings `TaskManager` / `Taskmanager` / `taskmanager` / `Task Manager`. The .NET assemblies (`Tjb.Web`, `Tjb.Api`, `Tjb.Data`, `Tjb.Migrations`, `Tjb.Shared`, `Tjb.UiTests`) and CFN template descriptions reading "TaskManager Bootstrap" coexist with each other and with AWS resources named `taskmanager-*`. The user's directive: scrub the lot, replace with `Tjb` / `tjb` per case convention.

This is a text refactor in the source repo, but it also forces AWS-side resource recreation for anything that carries the project name as part of its identifier (KMS aliases, SSM paths, Aurora cluster IDs). The text replacement is straightforward; the AWS cutover is where the real risk lives.

## Goals / Non-Goals

**Goals:**

- Zero occurrences of `TaskManager` / `Taskmanager` / `Task Manager` / `taskmanager` in committed source files after this change archives.
- AWS resource names align with the new convention: `tjb-` prefix on all project-named resources.
- All in-flight OpenSpec changes are updated consistently — no document references the old name.
- The migration is testable: a single `grep -rE 'TaskManager|Taskmanager|Task Manager|taskmanager' --include='*.{cs,md,yml,yaml,template,json,csproj,ps1,sh}' . | grep -v -E '/(obj|bin|node_modules)/'` returns no hits.

**Non-Goals:**

- Renaming the GitHub repository (`YadaYadaSoftware/awswebapp` — stays).
- Renaming GitHub Actions secrets (generic names, no project string).
- Renaming the four shared-infrastructure branches (`app`/`beta`/`alpha`/`dev`).
- Renaming IAM users (`GitHubActionsUser`, `GitHubActionsUserProd` — generic, no project name).
- Renaming the externally-managed `cf-templates-{account}-{region}` S3 bucket.
- "Smart" renames that affect generated build artifacts in `obj/` / `bin/` (these regenerate from source).
- Renaming files outside the audit (no fishing expedition into `docs/` or random `.txt` files; if a grep doesn't show a match, the file doesn't need editing).
- A full code review or refactor while we're in there.

## Decisions

### D1. Case-aware mapping table

The rename is a series of string substitutions, applied in order (most-specific first to avoid double-replacement):

| Find | Replace | Where it typically appears |
|---|---|---|
| `Task Manager` | `Tjb` | Prose in `.md` docs ("Task Manager UI tests…") |
| `Taskmanager` | `Tjb` | Rare; user's exact wording in the request |
| `TaskManager` | `Tjb` | C# namespaces in comments, appsettings JSON labels, CFN template descriptions, `src/docs/*.md` titles |
| `taskmanager` | `tjb` | AWS resource names (KMS aliases, SSM paths, IAM policy resource scopes, Aurora cluster identifiers, Secrets Manager paths) |

Applied via tooling rather than by hand:
```powershell
$root = "C:\Users\hound\awswebapp"
$globs = @("*.template","*.yml","*.yaml","*.md","*.cs","*.json","*.csproj","*.ps1","*.sh")
foreach ($pattern in @("Task Manager","Taskmanager","TaskManager","taskmanager")) {
  $replace = if ($pattern -ceq "taskmanager") { "tjb" } else { "Tjb" }
  Get-ChildItem -Path $root -Recurse -Include $globs `
    | Where-Object { $_.FullName -notmatch '\\(obj|bin|node_modules)\\' } `
    | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        if ($content -cmatch $pattern) {  # -cmatch = case-sensitive
          $new = $content -creplace $pattern, $replace  # -creplace = case-sensitive
          Set-Content -Path $_.FullName -Value $new -NoNewline -Encoding utf8
        }
      }
}
```

**Order matters because of containment**: `Task Manager` (with space) must run first or the `Taskmanager` pass would split it into `Tjb Manager` and orphan the second word. Then `Taskmanager` (no space, capital T) before `TaskManager` doesn't matter (they're distinct strings), but lowercase `taskmanager` must run *last* — otherwise it'd match the prefix of `TaskManager` if the regex weren't case-sensitive. Using `-creplace` (case-sensitive) makes the ordering less critical, but it's still safer to go most-specific to least-specific.

### D2. File-name renames

If any file has `TaskManager` in its name (PowerShell scripts like `Connect-TaskManagerDB.ps1`, JSON like `taskmanager-config.json`, etc.), `git mv` it so the file name follows the same convention. Audit step in tasks.md catches these.

### D3. Skip generated files

Don't touch:
- `**/obj/**` — MSBuild output, regenerated on every build.
- `**/bin/**` — same.
- `node_modules/` if present — third-party code we don't own.
- `**/.git/**` — git internals.

Per the audit grep, generated `AssemblyInfo.cs` files in `obj/Debug/` and `obj/Release/` contain old names (e.g., `TaskManager.Web.AssemblyInfo.cs`). These get overwritten on next `dotnet build` from the actual project name (`Tjb.Web.csproj`). Manually editing them is pointless.

### D4. CFN template parameter values vs hardcoded strings

Some hardcoded `taskmanager` literals are inside `!Sub` expressions that reference a parameter. For example, today's bootstrap.template might have:
```yaml
AliasName: "alias/taskmanager-aurora-nonprod"
```

The rename changes this to:
```yaml
AliasName: "alias/tjb-aurora-nonprod"
```

This is a straight string replacement — no parameter introduced. If the sibling `make-deployment-stack-reusable` change subsequently lands and parameterizes this to `!Sub "alias/${ProjectName}-aurora-nonprod"`, the consumer (this project) passes `ProjectName: tjb` and the deployed name is identical. Both changes are forward-compatible with each other regardless of land order.

### D5. AWS cutover ordering

The rename touches resources whose names are immutable in AWS (Aurora cluster identifiers, KMS alias names that consumers reference). The cutover ordering matters:

1. **Bootstrap stack first.** New KMS aliases (`alias/tjb-aurora-*`) get created; SSM parameters at the new paths (`/tjb/kms/*`) get populated. The old aliases (`alias/taskmanager-aurora-*`) are deleted by CloudFormation as part of the same update (CFN sees the renamed `AWS::KMS::Alias` resource and replaces it). The old SSM parameters at `/taskmanager/kms/*` are **NOT** deleted automatically because they're separate CFN resources with different `Name` properties — CFN creates the new and orphans the old. Manual cleanup task in tasks.md.

2. **Workflow update second.** The "Lookup bootstrap KMS key from SSM" step needs the new path. Update and merge so subsequent env-stack deploys use the new SSM path.

3. **Env stacks third (dev → beta → alpha → app).** Each env-stack redeploy creates new Aurora cluster identifiers (`tjb-<branch>-global-cluster` etc.) and Secrets Manager paths (`tjb/database/regional/*`). For non-prod, this is drop+recreate from the existing centralize-aurora-kms-keys/tasks.md Phase 3 procedure. For prod, the maintenance-window cutover from Phase 4 of the same plan applies.

4. **Cleanup.** Delete the old SSM parameters (`/taskmanager/kms/*`) and any orphaned KMS aliases that didn't get auto-deleted.

The bootstrap deploy is non-destructive at the KMS level (alias names change but underlying keys' ARNs are unchanged — clusters still use the same key, just the human-readable alias name changes). The env-stack redeploys ARE destructive for clusters (since identifiers are immutable).

### D6. OpenSpec doc updates are part of this change

The 5 in-flight OpenSpec changes (`centralize-aurora-kms-keys`, `make-deployment-stack-reusable`, `move-shared-lambda-role-to-bootstrap`, `robust-aurora-cluster-teardown`, `shift-secondary-region-to-us-east-2`) all reference the old name. They get scrubbed in this change too — the find/replace pass runs across all of `openspec/changes/**/*.md`.

For *modified capability deltas* (spec.md delta files in `specs/` directories of those changes): the requirement text and scenarios that mention `alias/taskmanager-*` etc. get updated to use `tjb`. These are spec-level behavioral statements about resource names, so they qualify as MODIFIED Requirements rather than as cosmetic doc changes.

### D7. Validation: grep-as-test

The single test that proves the rename is complete:

```powershell
Get-ChildItem -Path C:\Users\hound\awswebapp -Recurse -File `
  -Include *.cs,*.md,*.yml,*.yaml,*.template,*.json,*.csproj,*.ps1,*.sh `
| Where-Object { $_.FullName -notmatch '\\(obj|bin|node_modules|\.git)\\' } `
| Select-String -CaseSensitive -Pattern '(TaskManager|Taskmanager|Task Manager|taskmanager)' `
| Format-List Path, LineNumber, Line
```

Expected output: empty. Any non-empty result is a missed rename location.

## Risks / Trade-offs

- **[Risk] Aurora cluster identifiers change forces recreate of every env stack's cluster.** → Mitigate: leverage the existing recreate procedure (centralize-aurora-kms-keys/tasks.md Phase 3 for non-prod, Phase 4 for prod). Coordinate with stakeholders for app — same maintenance window pattern as before.
- **[Risk] Old SSM parameters at `/taskmanager/kms/*` get orphaned (CFN creates new, doesn't delete old).** → Mitigate: explicit `aws ssm delete-parameter` call in the cleanup phase of tasks.md.
- **[Risk] Build-artifact AssemblyInfo files contain old names temporarily.** → Mitigate: skip them during text replacement; first `dotnet build` after the rename regenerates them from the (already-correct) project names.
- **[Risk] In-flight OpenSpec docs are *narrative* in places — wholesale find/replace on docs might mangle sentence flow.** → Mitigate: after the automated replace, eyeball each diff in the openspec/changes/* files. Sentences like "TaskManager has a single bootstrap stack" become "Tjb has a single bootstrap stack" which is awkward; rewrite as "the project has a single bootstrap stack" or "Tjb's bootstrap stack" in those cases.
- **[Risk] The make-deployment-stack-reusable change's "no `taskmanager` literal" acceptance criterion becomes trivially true after this change.** → That's fine. It's still a valid acceptance test; we're just renaming what's being tested for.
- **[Risk] Connection-string database names in appsettings.json may reference a logical `TaskManager` database name that doesn't match the actual deployed DB names (which are `dev`/`alpha`/etc.).** → Audit during the .cs / .json rename pass; if any connection string uses `Database=TaskManager`, leave it as-is (it's a local-dev placeholder) OR change to `Database=Tjb` to match the rename intent. Defer the decision per-file.
- **[Trade-off] One large mechanical commit vs. a series of per-file-type commits.** Going with one big commit: easier to review the *intent* (single concept = "rename"), simpler to revert atomically if it goes wrong. Each touched file's diff is small (just string replacements) so review burden is bounded.

## Migration Plan

**Phase 1 — Source-tree rename (text replacement)**
1. Run the `-creplace` pass per D1 across all eligible file globs.
2. Manually review the diff for OpenSpec doc narrative awkwardness per the risk above.
3. `git mv` any files whose names contain `TaskManager`.
4. Build (`dotnet build`) to verify the .cs / .csproj changes compile and the `obj/**/AssemblyInfo.cs` files regenerate with new content.
5. Run `aws cloudformation validate-template` on each touched `*.template`.
6. Run `openspec validate --strict` on every change in `openspec/changes/`.

**Phase 2 — Commit + push (no AWS impact yet)**
7. Single commit with conventional-commit message `refactor: rename TaskManager/taskmanager to Tjb/tjb across the repo`.
8. Push to dev branch. CI runs but the workflow won't change behavior at this point — the SSM lookup still targets the *old* path (`/taskmanager/kms/*`), which is still populated, so dev's deploy is a no-op or trivial template-description-only update.

**Phase 3 — Bootstrap redeploy (AWS cutover begins)**
9. Operator runs `aws cloudformation deploy --stack-name bootstrap-appcloud-systems ...` manually with the updated template. CFN replaces the KMS aliases (`alias/taskmanager-*` → `alias/tjb-*`) and creates new SSM parameters at the new paths.
10. Verify new SSM parameters resolve: `aws ssm get-parameter --name /tjb/kms/nonprod/aurora-key-arn --region us-east-1`.
11. Repeat for us-west-2.
12. (At this point, both the old `/taskmanager/kms/*` and new `/tjb/kms/*` SSM parameters exist. Workflow still reads the old path.)

**Phase 4 — Workflow cutover**
13. Workflow already has the updated SSM path (from Phase 1's file rename). After Phase 3 succeeds, the next CI run picks up the new path. **If** there's a desire to test workflow ↔ AWS in isolation: push a feature branch first, watch CI logs to verify the new path resolves.

**Phase 5 — Env-stack cluster recreates**
14. dev: `aws cloudformation delete-stack --stack-name dev-appcloud-systems`, wait, redeploy via CI. New cluster has `tjb-dev-global-cluster` identifier.
15. alpha: same. (Snapshot data first if any.)
16. beta: same.
17. app: scheduled maintenance window; existing centralize-aurora-kms-keys/tasks.md Phase 4 procedure.

**Phase 6 — Cleanup**
18. Delete orphaned SSM parameters at the old paths: `aws ssm delete-parameter --name /taskmanager/kms/nonprod/aurora-key-arn --region <each>`. Same for `/prod/`.
19. Confirm grep-as-test from D7 returns empty.
20. Archive this change.

**Rollback:**
- Phase 1-2 reversible by `git revert`.
- Phase 3 reversible by reverting bootstrap.template and re-running `aws cloudformation deploy`. KMS aliases get renamed back; SSM params at `/taskmanager/*` recreated.
- Phase 5 reversible per env (recreate the cluster with the old identifier). Slow but mechanical.
- Phase 6 is the point of no return; old paths are gone.

## Open Questions

1. **Database connection-string label.** The `appsettings.Development.json` files likely have `"Database=TaskManager"` or similar in localhost connection strings. Should the local DB also be renamed to `Tjb`, or left as-is (it's just a local-dev value)? Suggest leaving the local-dev DB name as-is and just renaming any other appearances — the cost of a real local DB rename is low but unnecessary.
2. **Repo description / topic tags.** GitHub repo settings might list "TaskManager" in description / topics. Update separately via GitHub UI; out of scope for this change (no source file to edit).
3. **Existing AWS resources that don't get cleaned by CFN.** If old `taskmanager-*` SSM parameters or KMS aliases linger after Phase 3-6, do we need automated detection? Probably not — one-time cleanup is fine. A follow-up check after a few months: `aws ssm get-parameters-by-path --path /taskmanager` should return empty.
