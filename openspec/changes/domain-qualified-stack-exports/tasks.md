# Tasks — domain-qualified-stack-exports

> **Migration safety:** an export that is imported cannot be renamed/removed in place.
> The three phases below MUST deploy in order, each fully rolled out everywhere before
> the next begins. Phases 1 and 2 are individually safe resting points.

## 1. Prereqs

- [x] 1.1 Confirm `robust-aurora-cluster-teardown` has landed on `dev` (this change branches from a dev tip that already has the no-replacement db.template — avoids re-touching the same export lines on top of an in-flight cluster change).
- [x] 1.2 Enumerate every **live** feature-branch app stack currently importing dev's exports (`aws cloudformation list-stacks` + filter `*-appcloud-systems` minus the env stacks). These all need a Phase-2 redeploy (or teardown) before Phase 3. Record the list.
  - Recorded 2026-06-05 (us-east-1), excluding env stacks `dev-appcloud-systems` / `app-appcloud-systems` (and `alpha`/`beta`, not currently deployed):
    - `robust-aurora-cluster-teardown-appcloud-systems`
    - `testbranch-appcloud-systems`

## 2. Phase 1 — ADD domain-qualified exports (keep the old)

- [x] 2.1 In each exporter template, **add** a second `Output` (or dual-export is not allowed — see note) ... NOTE: a single resource value needs two exports under two names, which requires **two `Output` entries** with distinct logical IDs both pointing at the same `Value`. Add a `<Name>DomainQualified` output for every export listed in design.md D4, with `Export.Name: "<Name>-${BranchName}-${DomainDashed}"`.
- [x] 2.2 Templates to edit: db.template (11), network.template (10), web.template (5), infrastructure.template (2), api.template (2), security.template (1). _(Also added a `DomainName` parameter to network.template and api.template — they did not previously receive it — and wired `DomainName: !Ref DomainName` from backend.template→NetworkingStack and application.template→ApiStack.)_
- [x] 2.3 `aws cloudformation validate-template` each edited template. _(All 8 valid: db, network, web, infrastructure, api, security, backend, application.)_
- [~] 2.4 Deploy Phase 1 to **all** env backends: push to `dev` (single region) and to `alpha`/`beta`/`app` (multi-region). Confirm both old and new export names exist: `aws cloudformation list-exports --query "Exports[?contains(Name,'DatabaseHost')]"`.
  - **dev DONE (2026-06-05):** `dev-appcloud-systems` UPDATE_COMPLETE; 30 old `-dev` exports each have a `-dev-appcloud-systems` twin (30, not 31 — `AuroraGlobalClusterId` is multi-region-only).
  - **app PENDING:** deployed in us-east-1 only, 31 old `-app` exports, 0 domain-qualified. Needs a Phase-1-only deploy before Phase 2 reaches it.
  - **alpha/beta:** not currently deployed in either region.

## 3. Phase 2 — REPOINT all importers

- [x] 3.1 In each importer template, change every `Fn::ImportValue: !Sub "<Name>-${EnvironmentToImport}"` to the `${DomainDashed}`-suffixed form (design.md D2). Templates: api.template (8), web.template (14), dns.template (5 — `${BranchName}` same-env imports), db.template (3). _(Edits drafted; HELD uncommitted until Phase 1 is live on every backend that imports them.)_
- [x] 3.2 `aws cloudformation validate-template` each. _(api, web, dns, db all valid; 0 old-form imports remain.)_
- [~] 3.3 Deploy Phase 2 to env stacks (dev, then alpha/beta/app). Each consumer now resolves the new names; the old exports remain but go unused.
  - **dev DONE (2026-06-05):** `dev-appcloud-systems` UPDATE_COMPLETE after Phase 2 push (no rollback → qualified imports resolve). alpha/beta not deployed; app pending its own Phase 1 first.
- [x] 3.4 **Redeploy every live feature-branch app stack** from §1.2 so it imports the new names. Any feature branch not redeployed (or deleted) will still hold a reference to the old export and **block Phase 3**. Push each branch (or run its deploy) — or tear down stale branches.
  - Both §1.2 feature stacks were already torn down by the time Phase 2 landed (2026-06-05): `robust-aurora-cluster-teardown-appcloud-systems` and `testbranch-appcloud-systems` no longer exist.
  - Deleted the `testbranch` git branch (remote+local) so it can't redeploy old-name imports and block Phase 3.
  - `robust-aurora-cluster-teardown` branch still exists (wilhelm's) but its stack is gone, so it imports nothing now. **Flag:** if wilhelm redeploys it before Phase 3, merge dev (Phase 1+2) into it first so it imports the qualified names.
- [x] 3.5 Verify no stack still imports an old name: for a representative old export, `aws cloudformation list-imports --export-name "DatabaseHost-dev"` should return empty (or error "not imported").
  - Verified 2026-06-05: scanned **every** old `-dev` export — none is imported by any stack ("not imported by any stack" for all). dev is clear for Phase 3 removal.

## 4. Phase 3 — REMOVE the old un-qualified exports

- [x] 4.1 In each exporter template, delete the original `<Name>-${BranchName}` `Export` (keep only the domain-qualified one — and consider renaming the output logical IDs back to canonical now that there is only one). _(Removed all 31 bare-export `Output` blocks; kept the `*DomainQualified` outputs as-is. Did NOT rename logical IDs back to canonical — renaming an output's export name in the same deploy risks a transient duplicate-export error, and the suffix is cosmetic. Could be a follow-up cleanup once app is migrated.)_
- [x] 4.2 `aws cloudformation validate-template` each. _(All valid; 0 bare exports remain, 31 domain-qualified outputs intact.)_
- [ ] 4.3 Deploy Phase 3 to all env backends (dev, then alpha/beta/app). If any deploy fails with "export in use", a feature stack from §3.4 was missed — repoint/tear it down and retry. Do NOT force.
- [ ] 4.4 Confirm only domain-qualified exports remain: `aws cloudformation list-exports` shows no bare `<Name>-<branch>` names.

## 5. Validation

- [ ] 5.1 `openspec validate domain-qualified-stack-exports --strict` passes.
- [ ] 5.2 Deploy a throwaway feature branch end-to-end against the migrated dev backend; UI tests green (proves the app still resolves DB host/secret via the new import names).
- [ ] 5.3 (Optional, proves the original motivation) Dry-run a second-domain bootstrap+backend in a sandbox account/region and confirm no export-name collision.
- [ ] 5.4 Archive this change (`/opsx:archive`).
