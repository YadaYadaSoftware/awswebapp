## 1. Prereqs

- [ ] 1.1 Confirm the upstream three are landed: framework packages published, sample consuming them, sample deployed and observable at its URL.
- [ ] 1.2 Branch from `dev` with the spec's exact name: `git checkout dev; git pull; git checkout -b framework-slipstream-upgrade`.
- [ ] 1.3 Pick the representative demonstration change (design Q1) — a small, visually-obvious, reversible framework change (e.g. a shared-layout string).

## 2. Document the upgrade contract

- [ ] 2.1 Decide the documentation home (design Q2): extend `CONSUMING.md` if it exists, else `docs/framework-upgrades.md`.
- [ ] 2.2 Document versioning semantics over the existing GitVersion + branch-suffix mechanism: stable on `app`, `-{branch}` pre-releases elsewhere; patch/minor/major meaning for consumers.
- [ ] 2.3 Write the consumer upgrade runbook: bump `PackageReference`, restore, build, test, deploy; plus how to pin a branch-suffixed pre-release to validate a fix before it lands on `app`.
- [ ] 2.4 Write the breaking-change policy: what is breaking (removed/renamed extension, changed Identity base, changed required config key), how it is signaled (major bump + CHANGELOG), and the deprecation overlap window.
- [ ] 2.5 Add/adopt a `CHANGELOG.md` convention for `Tjb.Web.Framework`/`Tjb.Web.Hosting`.

## 3. Demonstrate the loop end-to-end

- [ ] 3.1 Make the representative change in the framework; publish a new framework package version via CI.
- [ ] 3.2 **Checkpoint (pre-release path):** on a sample feature branch, bump the sample's framework `PackageReference` to the new (branch-suffixed if pre-`app`) version; restore + build with no other sample edits.
- [ ] 3.3 Redeploy the sample feature branch; **observe the change in the deployed sample** at its URL.
- [ ] 3.4 **Checkpoint (isolation):** confirm TaskManager's deployment is unchanged (it did not bump its reference).
- [ ] 3.5 Record the demonstration (the published version, the one-line sample diff, the before/after observation) in the contract doc.
- [ ] 3.6 Revert the representative change (it was a demonstration vehicle), or keep it if it is a genuine improvement — note which.

## 4. Validation, archive

- [ ] 4.1 Verify the "fix flowed in by version bump alone" — if any sample *code* edit was required beyond the version number, file it as a coupling gap against `extract-web-framework-package` rather than absorbing it here.
- [ ] 4.2 `openspec validate framework-slipstream-upgrade --strict` and resolve issues.
- [ ] 4.3 Verify each scenario in `specs/framework-slipstream/spec.md`.
- [ ] 4.4 Archive this change (`/opsx:archive`) once merged and validated.
