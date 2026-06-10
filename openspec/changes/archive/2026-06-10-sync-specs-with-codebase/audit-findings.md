# Spec/Doc ↔ Codebase Sync Audit — Findings Record

Branch: `sync-specs-with-codebase` (cut from `dev` @ 29ab354)
Scope: every tracked `.md`/`.yaml`/`.yml` file, excluding any under an `archive/` directory segment.

Verdicts: `in-sync` · `updated` · `flagged-as-possible-bug` · `skipped-archived` · `skipped-removed`

## Scope counts

- Total tracked `.md`/`.yaml`/`.yml` at audit start: 133
- Excluded (`archive/` segment): 57 → all `skipped-archived` (under `openspec/changes/archive/**`)
- In-scope (examined): 76
  - `updated`: 41
  - `in-sync`: 27
  - `flagged-as-possible-bug`: 3 (spec/doc matches code; the **code** looks wrong — not silently rewritten)
  - `skipped-removed`: 6 (`.kilocode/**` deleted by the user mid-audit)

## In-scope worklist & verdicts

### `openspec/specs/**` (canonical current-state specs)

| File | Verdict | Note |
|------|---------|------|
| aurora-cluster-teardown/spec.md | updated | CloudWatch Logs IAM is `arn:aws:logs:${Region}:${AccountId}:*`, not "own log group only" |
| aurora-kms-key-management/spec.md | updated | Removed nonexistent `HasKeyAdmin`/`KeyAdminPrincipalArn` (contradicted by its own later text + template); IAM user/role names are `${AWS::StackName}-`prefixed |
| aws-ses-email-integration/spec.md | updated | Config binds the `AwsSes` section (`AwsSes__Region`/`AwsSes__SenderEmail`), not `AWS_REGION`/`AWS_SES_SENDER_EMAIL` |
| branch-stack-cleanup/spec.md | flagged-as-possible-bug | See B1, B2 below — spec matches code; code is the suspect |
| ci-run-concurrency/spec.md | in-sync | |
| cross-stack-export-naming/spec.md | in-sync | |
| default-branch-workflow-promotion/spec.md | in-sync | |
| domain-derived-resource-naming/spec.md | updated | `DomainName` is **dot form** (`appcloud.systems`); all derivations `!Split [".", …]`. Spec had claimed dashed form + `!Split ["-", …]`. Fixed param block, MasterUsername, examples, workflow scenarios |
| email-confirmation-workflow/spec.md | in-sync | |
| email-template-system/spec.md | updated | View model is `Email` + `ConfirmationUrl` only (no `RecipientName`/`ConfirmationLink`) |
| resource-tagging/spec.md | in-sync | |
| test-result-reporting/spec.md | updated | Unit TRX runs against the solution w/ `--filter !~Tjb.UiTests`; Playwright tracing is unconditional (`BaseTest`), not `on-first-retry`/`only-on-failure` |

### `openspec/changes/{active}/**` (in-flight plans — current-state claims only)

| File | Verdict | Note |
|------|---------|------|
| make-deployment-stack-reusable/proposal.md | updated | Premise corrected: `taskmanager` literals already removed; remaining target is `appcloud.systems`/`Tjb.Web` + a `ProjectName` param |
| make-deployment-stack-reusable/design.md | updated | D4 baseline note: substitution sites already `DomainName`/`${AWS::StackName}`-derived |
| make-deployment-stack-reusable/tasks.md | in-sync | |
| make-deployment-stack-reusable/specs/reusable-deployment-stack/spec.md | in-sync | |
| make-deployment-stack-reusable/.openspec.yaml | in-sync | |
| move-shared-lambda-role-to-bootstrap/proposal.md | updated | Role name/SSM path → `${AWS::StackName}-…` (the `taskmanager-…` name would trip the CI grep guard once implemented) |
| move-shared-lambda-role-to-bootstrap/design.md | updated | same name/path alignment |
| move-shared-lambda-role-to-bootstrap/tasks.md | updated | same; plus KMS SSM path form |
| move-shared-lambda-role-to-bootstrap/specs/shared-lambda-role-management/spec.md | updated | same name/path alignment |
| move-shared-lambda-role-to-bootstrap/.openspec.yaml | in-sync | |
| stabilize-ui-tests/specs/ui-test-stability/spec.md | updated | `on-first-retry` → unconditional tracing in `BaseTest` |
| stabilize-ui-tests/{proposal,design,tasks}.md, .openspec.yaml | in-sync | |

### Top-level prose & config

| File | Verdict | Note |
|------|---------|------|
| README.md | updated | Full rewrite: net10 / Aurora MySQL / ECS Fargate+ALB+ECR / `Tjb.*` / Identity+Google OAuth / branch model. Removed .NET8/PostgreSQL/Lambda/TaskManager.* |
| BRANCH_MANAGEMENT_README.md | updated | Stale `c:\Users\17034…` path; nonexistent `push-changelog.ps1`; changelog-automation claim corrected to actual CI behavior; cleanup bucket note |
| BROTHERS.md | in-sync | All scripts/names/homestead/behaviors verified |
| CLAUDE.md | in-sync | Used as ground-truth anchor; code consistently matched it (not independently re-audited line-by-line) |
| changelog.md | flagged-as-possible-bug | Generated artifact, structurally corrupted (raw `changes.md` pasted in); the auto-maintenance the README described isn't implemented in CI. Not hand-rewritten (it's generated) — see F3 |
| report.md | in-sync | One-time autonomous-run log; transient, not maintained; technical refs match code |
| openspec/config.yaml | updated | `Domain: e-commerce platform` → task/project management; tech-stack line expanded |
| GitVersion.yml | in-sync | `main` regex `^app$` matches the branch model |
| .yamllint.yml | in-sync | Self-description matches contents |

### `.github/workflows/**`

| File | Verdict | Note |
|------|---------|------|
| zbuild.yml | flagged-as-possible-bug | See B3 — comments otherwise accurate; not edited (behavior-gated) |
| cleanup-on-branch-delete.yml | flagged-as-possible-bug | See B1, B2 — not edited (behavior-gated) |

### `.claude/**` (tooling docs)

| File | Verdict | Note |
|------|---------|------|
| commands/new-brother.md | updated | Worktree base priority is `.bare` → `dev` → repo |
| commands/opsx/apply.md | updated | Removed dangling `/opsx:continue` reference |
| skills/openspec-apply-change/SKILL.md | updated | Removed dangling `openspec-continue-change` reference |
| commands/{brothers,next,whoami}.md | in-sync | minor homestead-list incompleteness, not codebase drift |
| commands/opsx/{archive,explore,propose}.md | in-sync | |
| skills/openspec-{archive-change,explore,propose}/SKILL.md | in-sync | |

### `src/docs/**`

| File | Verdict | Note |
|------|---------|------|
| ARCHITECTURE.md | updated | Full rewrite to ECS Fargate/Aurora MySQL/`Tjb.*` |
| AWS_DEPLOYMENT_GUIDE.md | updated | Full rewrite to real SAM/CFN+Docker/ECR pipeline |
| AWS_DEPLOYMENT_SETUP.md | updated | Full rewrite (secrets, repo vars, bootstrap, naming) |
| DEPLOYMENT_STRATEGY.md | updated | Full rewrite (nested-CFN→ECS; removed Lambda-Annotations model) |
| HYBRID_DEPLOYMENT_GUIDE.md | updated | Rewritten as correction (no CFN+SAM-Lambda hybrid exists) |
| IMPLEMENTATION_PLAN.md | updated | Rewritten to current implementation state |
| LAMBDA_ANNOTATIONS_NOTES.md | updated | Rewritten: Lambda abandoned for ECS; `Tjb.Api` vestigial |
| RDS_PROXY_ACCESS_GUIDE.md | updated | Rewritten as MySQL access guide (no RDS Proxy exists) |
| VSCODE_POSTGRESQL_SETUP.md | updated | Rewritten for MySQL (filename left as legacy) |
| WEB_DEPLOYMENT_STRATEGY.md | updated | Rewritten to actual ECS Fargate deploy of `Tjb.Web` |
| AWS_CREDENTIALS_SETUP.md | updated | ECR/ECS/SSM/KMS perms; bootstrap-created CI users; `app` not `main` |
| DATABASE_ACCESS_GUIDE.md | updated | MySQL/3306 throughout; `Tjb.Migrations`; stack/secret naming |
| DATABASE_MIGRATIONS_GUIDE.md | updated | `Tjb.Migrations` paths, migration file, EF commands; de-Lambda'd |
| DEVELOPMENT_TROUBLESHOOTING.md | updated | `Tjb.*` process/project names |
| GITHUB_SECRETS_SETUP.md | updated | Added missing required secrets; ECR/ECS IAM; `app` branch |
| GOOGLE_OAUTH_SETUP.md | updated | `https://{branch}.{DOMAIN}/signin-google`; `GOOGLE_CLIENT_*`; `Tjb.*` paths |
| INVITATION_SYSTEM_GUIDE.md | updated | Migration file + seed pointer + app name + base URL |
| GIT_SECRET_REMOVAL_COMMANDS.md | in-sync | Generic git remediation; no codebase refs |
| src/Tjb.UiTests/README.md | updated | `TaskManager` → `Tjb`; fixed csproj path |
| src/Tjb.Web/wwwroot/css/open-iconic/README.md | in-sync | Vendored third-party library doc |

### Removed mid-audit

| File | Verdict |
|------|---------|
| .kilocode/rules/*.md (6 files) | skipped-removed (user deleted `.kilocode`) |

## flagged-as-possible-bug (code looks wrong vs. intent — NOT silently encoded into docs)

- **B1 — cleanup mutex broken for slash-named branches.** `cleanup-on-branch-delete.yml:15` sets `concurrency.group: deploy-us-east-1-${{ github.event.ref }}` (full branch name, e.g. `feature/foo`), but the deploy job's group uses the **branch leaf** (`deploy-us-east-1-foo`). For any `type/name` branch the two groups don't match, so cleanup is NOT serialized against an in-flight deploy — the exact mutex the `branch-stack-cleanup` spec requires ("Serialize against any in-flight deploy"). Symptom: a delete-triggered teardown can run concurrently with a same-branch deploy. Fix is a workflow change (gated) — see follow-up FU1.
- **B2 — cleanup empties the wrong S3 bucket.** `cleanup-on-branch-delete.yml:290` uses `BUCKET="cf-templates-${ACCOUNT_ID}-us-east-1"`, but per-branch packaged templates actually live in the bootstrap-owned bucket `${ACCOUNT_ID}-${dashed-domain}-us-east-1` (`bootstrap.template:37`; deploy uploads there in `zbuild.yml`). So the branch's template prefix is never cleaned. The `branch-stack-cleanup` spec and `BRANCH_MANAGEMENT_README` historically carried the same `cf-templates-…` string (spec left matching code; README annotated). Fix is a workflow change (gated) — see FU2.
- **B3 — dead "latest" tag block.** `zbuild.yml:622-626` is gated on `if [ "$BRANCH_NAME" = "main" ]`, but this repo has no `main` branch leaf (production is `app`); the block never fires, so `:latest` is never pushed. The comment ("# Also tag as latest for main branch") accurately describes the dead code, so it was NOT edited (editing only the comment would make it lie about the code). Fix is a one-word condition change (gated) — see FU3.

## Out-of-scope follow-ups (require code/CI/script changes — gated by CLAUDE.md, NOT done here)

- **FU1** — Fix B1: align `cleanup-on-branch-delete.yml` concurrency group to the branch **leaf** (`${REF##*/}`) so it matches the deploy job.
- **FU2** — Fix B2: point the cleanup S3 step at `${ACCOUNT_ID}-${PROCESSED_DOMAIN}-us-east-1` (derive `PROCESSED_DOMAIN` as the deploy job does).
- **FU3** — Fix B3: change the latest-tag condition from `main` to `app` (or remove the dead block).
- **FU4** — RESOLVED (deleted). `Connect-AuroraDB.ps1` carried stale `taskmanager-*` stack/secret names, but the deeper problem was that it tunnels through a bastion host absent from the IaC (`bastion`/`BastionHostIP`/`TaskManager-Bastion` appear repo-wide ONLY in this script; no bastion resource or `BastionHostIP` output exists in any `infrastructure/*.template`). Maintainer confirmed the access path is dead; the script was deleted on `fix/ci-workflow-cleanup-bugs` (commit `04fb949`) rather than papered over.
  - **FU4-followup (open)** — Dangling references to the deleted script remain in docs that reference DB access: `CLAUDE.md` ("Connect to the deployed Aurora DB through the bastion"), `src/docs/DATABASE_ACCESS_GUIDE.md`, `src/docs/RDS_PROXY_ACCESS_GUIDE.md`, `src/docs/VSCODE_POSTGRESQL_SETUP.md`. These need their DB-access sections reconciled once the current access method (if any) is known. `appsettings.Development.json` also still uses DB name `TaskManagerDb` (prod/migrations use `TjbDb`) — a separate small drift.
- **FU5** — Stale leftover `Npgsql.EntityFrameworkCore.PostgreSQL` package reference in `src/Tjb.Data.csproj` (unused; runtime uses `UseMySql`). Cleanup candidate (a code change).
