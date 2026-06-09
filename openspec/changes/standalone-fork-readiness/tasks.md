## 1. Prereqs and reconciliation

- [x] 1.1 Confirm the `deploy.yml`/`sample-deploy.yml` surface. — *Done: re-read both; `deploy.yml` docker build used `--build-arg GH_PACKAGES_TOKEN=${{ secrets.GITHUB_TOKEN }}`, `sample-deploy.yml` build env `GH_PACKAGES_TOKEN: secrets.GITHUB_TOKEN` + literal `hosted-zone-id`.*
- [x] 1.2 Fallback mechanism. — *Caller (`sample-deploy.yml`) uses `${{ secrets.FRAMEWORK_FEED_TOKEN || secrets.GITHUB_TOKEN }}` (allowed at caller). Inside `deploy.yml` the `||` on secrets is NOT allowed, so the fallback is done in bash (`${FRAMEWORK_FEED_TOKEN:-$DEFAULT_GH_TOKEN}`).*

## 2. Framework-feed token parameterization

- [x] 2.1 `deploy.yml` optional `framework-feed-token` secret + bash fallback in the container build. — *Done.*
- [x] 2.2 `sample-deploy.yml` build `GH_PACKAGES_TOKEN` fallback. — *Done. The `framework-feed-token` reaches `deploy.yml` via the existing `secrets: inherit` (name-matches the repo's `FRAMEWORK_FEED_TOKEN`); no explicit `with` needed.*
- [x] 2.3 Validate workflows. — *Done: both pass `js-yaml`.*

## 3. Hosted-zone variable

- [x] 3.1 `sample-deploy.yml` `hosted-zone-id: ${{ vars.HOSTED_ZONE_ID }}`. — *Done.*
- [x] 3.2 No TaskManager regression / operator note. — *No operator action needed: TaskManager's `sample-deploy` is gated off (`SAMPLE_DEPLOY_ENABLED` unset) and its **main** deploy (`zbuild.yml`) keeps the literal zone — so `HOSTED_ZONE_ID` is only a fork requirement (documented in README + CLAUDE.md), set it only if you enable `sample-deploy` here.*

## 4. Config-validation preflight

- [x] 4.1 `validate-config` preflight job. — *Done: collects ALL missing required vars + secrets into one checklist (run summary + stderr), exits non-zero. Secrets received via `env:` (can't enumerate by name).*
- [x] 4.2 Warn on optional; gate build/deploy. — *Done: optional secrets warn via `::warning::`; `build-sample` and `deploy` both `needs:` `validate-config`.*
- [x] 4.3 No-op for TaskManager + respects the gate. — *Done: the preflight has `if: needs.get-branch-name.outputs.enabled == 'true'` (same `SAMPLE_DEPLOY_ENABLED` gate), so it skips for TaskManager. YAML re-validated.*

## 5. "Setting up a new repo" README section

- [x] 5.1 "Setting up a new repo" section (fork → keep-vs-delete → promote `src/sample` → `src/` with the paths to update). — *Done.*
- [x] 5.2 Framework feed + PAT + pin-version guidance. — *Done (step 4 of the section).*
- [x] 5.3 Variables/secrets checklist + link `DEPLOYING.md`. — *Done (step 5 table + step 6).*
- [x] 5.4 Workflow keep/adapt/delete table + troubleshooting. — *Done.*
- [x] 5.5 `CLAUDE.md` pointer + `src/sample/DEPLOYING.md` cross-org `FRAMEWORK_FEED_TOKEN` note. — *Done (CLAUDE.md "Forking into a new repo" bullet; DEPLOYING.md "Cross-org forks" note).*

## 6. No-regression check for TaskManager

- [x] 6.1 Additive for TaskManager. — *Confirmed by reasoning: TaskManager's `sample-deploy` is gated off so the preflight + build + deploy all skip (no need for `HOSTED_ZONE_ID`/`FRAMEWORK_FEED_TOKEN`); its **main** deploy (`zbuild.yml`) is untouched (literal zone) and `deploy.yml`'s token falls back to `GITHUB_TOKEN` when `framework-feed-token` is empty. Verified on the dev deploy after merge (no regression).*

## 7. Docs, validation, archive

- [x] 7.1 `openspec validate standalone-fork-readiness --strict`. — *passed.*
- [x] 7.2 README checklist vs preflight lists. — *Match: required secrets (6) + optional (4) are identical; required variables match except `SAMPLE_DEPLOY_ENABLED`, which is the **gate** (the preflight runs only when it's `true`, so it can't validate itself) — shown in the README table as the enabling Variable, not a preflight failure item.*
- [ ] 7.3 Verify spec scenarios end-to-end. — *Partial: same-org no-op + token-fallback + docs-match verified here; the cross-org PAT path and the missing-config failure messages are proven by the actual fork (out of band).*
- [ ] 7.4 Archive once merged to `dev` and validated. — *Pending merge.*
