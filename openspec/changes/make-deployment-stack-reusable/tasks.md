## 0. AUTONOMOUS-RUN STATUS — read first

This change was partially implemented during an autonomous feature-branch run. **Phase 2
(NuGet packaging) is done; Phases 1, 3, and 4 are deferred and need your decisions** — see
the per-task notes and the "Implementation notes" at the bottom. Two reasons this spec is
only partially auto-implementable:

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

- [ ] 1.1 Confirm foundational changes landed/deferred. — *`move-shared-lambda-role-to-bootstrap` is implemented on its own branch (not merged); ordering per design still applies.*
- [ ] 1.2 Decide + document the final NuGet package name. — *NEEDS DECISION. Used working name `YadaYada.AwsWebApp.DeploymentStack` as a PREVIEW (not locked; only branch-suffixed pre-release versions publish).*
- [ ] 1.3 Decide + document the AWS-account model (one-consumer-per-account vs namespaced). — *NEEDS DECISION before any v1.0.0 tag.*
- [x] 1.4 Audit hardcoded project-specific values. — *Done: `taskmanager` → 0 hits in `infrastructure/`. `appcloud.systems` hits are descriptions + a few `Default:` values on `DomainName` params (api/application/dns/infrastructure). `Tjb` hits are genuinely project-specific source paths in `api.template` (CodeUri/handler) and descriptions.*

## 2. Phase 1 — Template parameterization

- [ ] 2.1–2.6 Add `ProjectName` parameter across templates; replace `taskmanager-*`. — *DEFERRED pending the §0.1 design decision. The `taskmanager` literals are already gone (derived from `${AWS::StackName}`); introducing a separate `ProjectName` axis would diverge from the convention and should be decided first. Remaining genuinely-project-specific bits: `DomainName` `Default: "appcloud.systems"` (remove so consumers must specify) and `api.template`'s `Tjb.Api` CodeUri/handler.*
- [ ] 2.7–2.11 Workflow ProjectName override, validate, deploy checks, grep audit. — *DEFERRED with 2.1–2.6.*

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
