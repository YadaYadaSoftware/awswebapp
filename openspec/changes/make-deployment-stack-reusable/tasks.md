## 0. AUTONOMOUS-RUN STATUS — read first

Status: **Phase 1 (template parameterization) and Phase 2 (NuGet packaging) are done.**
§0.1 was decided *derive-from-domain* (no separate `ProjectName` axis) and the package name
is locked; `api.template` was dropped from the reusable contract. **Phases 3 (reusable
`workflow_call` extraction) and 4 (docs/release) remain, plus task 1.3 (account model) and
3.6/5.3 (release tags).** Phase 3 is the highest-risk change and should be done attended on a
throwaway branch with a watched deploy. Original autonomous-run context follows. Two reasons
this spec was only partially auto-implementable:

1. **Phase 1 is largely already satisfied.** `grep -rn 'taskmanager' infrastructure/` returns
   **zero hits** — the bootstrap consolidation + `domain-qualified-stack-exports` already moved
   all naming to `${AWS::StackName}` / `DomainName`-derived forms. The spec's premise (strip
   `taskmanager-*` literals) is effectively complete. What remains — adding a *separate*
   `ProjectName` parameter distinct from the domain — **conflicts with the established
   convention** (CLAUDE.md: names derive from the dashed domain = `${AWS::StackName}`). That's
   a design decision, not a mechanical edit. **Need your call:** keep deriving from the domain
   (recommended; ProjectName == dashed domain), or introduce a distinct ProjectName axis?
2. **Decision gates + risk.** Tasks 1.2 (lock the final package name — "hard to change") and
   1.3 (confirm the AWS account model before tagging v1.0.0) are explicit human decisions.
   Phase 3 (rewriting the entire `deploy` job into a reusable `workflow_call` + turning
   `zbuild.yml` into a thin caller) is the single highest-risk change in the backlog and was
   not done blind/unattended.

## 1. Prereqs and audit

- [x] 1.1 Confirm foundational changes landed/deferred. — *Confirmed: `move-shared-lambda-role-to-bootstrap` is implemented on its own branch (not merged); ordering per design still applies. `centralize-aurora-kms-keys` is archived/deployed. This change proceeds ahead of the others per the design's working order.*
- [x] 1.2 Decide + document the final NuGet package name. — *DECIDED: `YadaYada.AwsWebApp.DeploymentStack` is locked as the final name (already the csproj's package id). Only branch-suffixed pre-releases publish until a release tag.*
- [ ] 1.3 Decide + document the AWS-account model (one-consumer-per-account vs namespaced). — *NEEDS DECISION before any v1.0.0 tag.*
- [x] 1.4 Audit hardcoded project-specific values. — *Done: `taskmanager` → 0 hits in `infrastructure/`. `appcloud.systems` hits are descriptions + a few `Default:` values on `DomainName` params (api/application/dns/infrastructure). `Tjb` hits are genuinely project-specific source paths in `api.template` (CodeUri/handler) and descriptions.*

## 2. Phase 1 — Template parameterization

- [x] 2.1–2.6 Parameterize remaining project-specific literals (no separate `ProjectName` axis — §0.1 DECIDED: derive from domain). — *DONE: removed `DomainName` `Default: "appcloud.systems"` from `master`/`application`/`web`/`dns` (consumers must now supply it; safe — every parent passes `DomainName: !Ref DomainName` to nested stacks and the workflow passes it to top-level deploys). Derived the SES sender to `!Sub "noreply@${DomainName}"` in `web.template`. CLAUDE.md SES note synced. `api.template` (Tjb.Api-specific `CodeUri`/handler) DECIDED dropped from the reusable contract — excluded from the package via `Exclude` in the csproj (verified: the `.nupkg` now ships 10 templates, no `api.template`). Tjb.Api stays in the repo (vestigial) but is not part of the reusable stack.*
- [x] 2.7–2.11 Workflow override, validate, deploy checks, grep audit. — *DONE (code side): under derive-from-domain there is no `ProjectName` override to add; the workflow already passes `DomainName=${{ secrets.DOMAIN_NAME }}` to every deploy, so no caller change is needed. `grep -rn taskmanager infrastructure/` = 0 hits. **Deploy verification: PASSED** — commit `b452d6c` pushed; the `Deploy Everything` CI run `27206056588` completed `success` in 13m19s, deploying to `https://make-deployment-stack-reusable.appcloud.systems` (removed `DomainName` defaults + derived SES sender produced no visible breakage).*

## 3. Phase 2 — NuGet package the templates

- [x] 3.1 Create `src/YadaYada.AwsWebApp.DeploymentStack/…csproj` — content-only package (`IsPackable`, `IncludeBuildOutput=false`), packing every file in `infrastructure/` to `contentFiles/any/any/infrastructure/`.
- [x] 3.2 Add the project to [Tjb.sln](../../../Tjb.sln). — *`dotnet sln add`.*
- [x] 3.3 Build + inspect the `.nupkg`. — *Verified: `dotnet pack` produces `contentFiles/any/any/infrastructure/*.template` for all templates (bootstrap, master, backend, db, network, infrastructure, web, api, dns, security).*
- [x] 3.4 Add a `Pack YadaYada.AwsWebApp.DeploymentStack` step to the build job mirroring the existing Pack steps' SemVer + branch-suffix convention (content-only → no `--include-symbols/--include-source`).
- [x] 3.5 The existing `publish-nuget` job picks up everything in `./nupkgs/` — no publish-job change needed. — *Confirmed by inspection; verify on the next CI run.*
- [ ] 3.6 Tag a pre-release and confirm on GitHub Packages. — *Not done autonomously (tagging is a release decision). The branch build will publish a branch-suffixed pre-release automatically.*

## 4. Phase 3 — Extract the reusable workflow

- [ ] 4.1–4.6 Create `.github/workflows/deploy.yml` (`workflow_call`), move the `deploy` job into it, parameterize SSM paths/stack names by input, add a template-extraction step, and rewrite `zbuild.yml` as a thin caller. — *DEFERRED: highest-risk change in the backlog (full deploy-pipeline rewrite). Not done blind in an unattended run; recommend implementing attended, on a throwaway branch, with a live deploy watched end-to-end (task 4.5).*

## 5. Phase 4 — Documentation and release

- [ ] 5.1 Write `CONSUMING.md`. — *DEFERRED: it must document the reusable workflow's input/secret surface, which doesn't exist until Phase 3 lands. Writing it now would be fiction/drift.*
- [ ] 5.2 Update README.md / CLAUDE.md to note the repo is also a library. — *DEFERRED until the consumable surface (Phase 3) exists.*
- [ ] 5.3 Tag `v1.0.0` on `app`. — *DEFERRED (release decision; gated on 1.2/1.3).*
- [ ] 5.4 Announce internally. — *DEFERRED.*

## 6. Validation

- [x] 6.1 `openspec validate make-deployment-stack-reusable --strict`. — *passed.*
- [ ] 6.2 Verify each spec scenario against the deployed system. — *Partially: the packaging scenarios (package builds, templates extractable from `contentFiles/any/any/infrastructure/`) are verified locally. Reusable-workflow / consumer scenarios are blocked on Phase 3.*
- [ ] 6.3 Archive this change. — *Intentionally NOT done (feature-branch-only run; no merge/archive). Also, this change is only partially implemented — it should not be archived until Phases 1/3/4 land.*

## Implementation notes (autonomous run on branch `make-deployment-stack-reusable`)

- **Delivered (safe, additive, decision-light):** the template NuGet package (`YadaYada.AwsWebApp.DeploymentStack`, preview name) + sln entry + a CI Pack step. The existing publish job will push a branch-suffixed pre-release. No deploy-path behavior changed, so the feature-branch deploy is unaffected by this change.
- **Deferred for your input:** the `ProjectName`-vs-`${AWS::StackName}` design decision (§0.1), the package-name lock-in (1.2), the account-model decision (1.3), the high-risk reusable-workflow extraction (Phase 3), and the docs/release (Phase 4, which depend on Phase 3).
- **Recommendation:** treat Phase 2 as a foundation; tackle Phase 3 attended on a throwaway branch with a watched deploy, after deciding the naming axis and package name.
