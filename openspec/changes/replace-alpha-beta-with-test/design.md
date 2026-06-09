## Context

Shared environments are expressed as long-lived branches. Today:
- `app` — production, multi-region (Aurora Global Cluster, **prod** KMS key), `master.template`.
- `beta` — shared non-prod, multi-region (nonprod KMS), `master.template`.
- `alpha` — shared non-prod, multi-region (nonprod KMS), `master.template` — **identical in shape to `beta`**.
- `dev` — integration, **single-region**, nonprod KMS, `master.template`.

`beta` and `alpha` are functionally identical non-prod environments. The branch/env membership is encoded in several places (the touchpoints found by grep): `zbuild.yml` (branch tests + the `["dev","alpha","beta","app"]` deploy gate), `deploy.yml` (`"app beta alpha"` multi-region default + `"app beta alpha dev"` shared default), `cleanup-on-branch-delete.yml` (protected list), `db.template` (`EnvironmentToImport` ∈ {beta, alpha, dev} enables the Data API HTTP endpoint), `master.template`/`application.template` (multi-region descriptions), and the brothers tooling (`_BrothersCommon.ps1`, `Get-Brothers.ps1`, `merge.sh` homestead list). The KMS prod/nonprod selection in `aurora-kms-key-management` is already name-agnostic (`app`→prod, every other branch→nonprod), so it needs no logic change.

## Goals / Non-Goals

**Goals:**
- One shared non-prod environment, `test`, replacing both `beta` and `alpha`.
- Final shared set: `app` (prod, multi-region) + `test` (non-prod, multi-region) + `dev` (non-prod, single-region).
- Zero residual `alpha`/`beta` environment references in workflow, infra, scripts, app config, or docs.
- Decommission the live `beta` and `alpha` stacks + their Aurora Global Clusters cleanly.

**Non-Goals:**
- Renaming `app` or `dev`.
- Changing the domain-derived naming or the KMS key mechanism (already name-agnostic).
- Changing single-region `dev`'s topology.

## Decisions

**D1. `test` is multi-region, mirroring the old `beta`/`alpha` topology.**
`test` joins `app` in the multi-region set (`master.template`, Aurora Global Cluster across primary+secondary, nonprod KMS key). Rationale: preserve a production-like (multi-region) shared non-prod environment for pre-prod validation; `dev` remains the cheap single-region integration env. Alternative considered — make `test` single-region — rejected: it would lose multi-region pre-prod coverage that `beta`/`alpha` provided before `app`.

**D2. Branch→scope mapping stays rule-based, not enumerated.**
Keep `app`→prod-KMS, every-other-branch→nonprod-KMS (unchanged). Where the code *enumerates* the shared/multi-region set, replace the `beta`/`alpha` entries with `test` (`"app test"` multi-region; `"app test dev"` shared). The `EnvironmentToImport` Data-API condition `{beta, alpha, dev}` → `{test, dev}`.

**D3. GitVersion `alpha`/`beta` prerelease labels are in scope and get renamed.**
`GitVersion.yml` uses the literal labels `alpha`/`beta` as semver *prerelease tags* on `release`/`hotfix` branches — distinct from the deployment environments, but the user's goal is to remove the `alpha`/`beta` concept *entirely*. Rename these prerelease labels (e.g. `release`→`rc`, `hotfix`→`hotfix`) so no `alpha`/`beta` token remains. This changes prerelease version strings only; it does not affect the `main`(`app`)/`develop`(`dev`) mappings. (Flagged as a minor, reversible decision — see Open Questions.)

**D4. Roll out by standing up `test` before decommissioning `beta`/`alpha`.**
Order: (1) land the source change (workflow/infra/scripts/docs on the `test` model), (2) create the `test` branch and let it deploy a fresh multi-region `test` environment, (3) **manually** decommission `beta` and `alpha` (they are protected branches — the cleanup workflow never auto-deletes them). This keeps a shared non-prod env available throughout.

**D5. Decommission uses the global-cluster-aware teardown.**
Tearing down `beta`/`alpha` means deleting two multi-region env stacks, each with an Aurora Global Cluster. Use the now global-cluster-aware `robust-aurora-cluster-teardown` Lambda (detaches members before delete) — i.e. delete the secondary-region stack, then the primary-region stack, per region, for each of `beta` and `alpha`. Bootstrap (shared, per-region) is untouched.

## Risks / Trade-offs

- **[Risk] A missed `alpha`/`beta` reference leaves dead/at-odds logic.** → Mitigation: a grep gate (`\b(alpha|beta)\b` over workflow/infra/scripts/app/config, excluding the GitVersion semver labels once renamed and `openspec/`/docs-history) must return zero before done; tasks include it.
- **[Risk] Decommissioning `beta`/`alpha` Aurora Global Clusters repeats the manual detach pain.** → Mitigation: the global-cluster detach fix is now live in bootstrap (both regions), so `delete-stack` drains+detaches automatically.
- **[Risk] Removing a shared non-prod env reduces pre-prod surface.** → Mitigation: `test` retains the multi-region topology; net effect is one non-prod env instead of two identical ones.
- **[Trade-off] GitVersion label rename changes prerelease version strings** for release/hotfix builds. → Acceptable; no consumer pins to `-alpha`/`-beta` prerelease NuGets (feature branches use `{version}-{sanitized-branch}`).

## Migration Plan

1. Implement the source change on this branch (workflow, infra, scripts, app config, docs, GitVersion).
2. Merge to `dev`; verify dev deploy stays green (single-region, unaffected by the multi-region set change beyond the enumerations).
3. Create the `test` branch from `dev`; confirm it deploys a multi-region `test` env (both regions, Aurora Global Cluster, nonprod KMS) and is healthy.
4. Manually decommission `beta` then `alpha` (secondary-region stack → primary-region stack, each region), confirming Aurora Global Clusters drain via the teardown Lambda.
5. Delete the `beta` and `alpha` branches.
6. Promote the change to `app`.

**Rollback:** before step 4, revert is trivial (re-add `beta`/`alpha` to the configs). After decommissioning, restoring `beta`/`alpha` would mean recreating their stacks — avoid by validating `test` first.

## Open Questions

1. **GitVersion labels** — confirm D3 (rename `alpha`/`beta` prerelease labels) vs. leaving them as semver-only conventions out of scope. Default: rename, for total eradication.
2. **`test` URL/cert** — `test` deploys to `https://test.{DOMAIN_NAME}`; confirm the wildcard/ACM cert and Route53 cover `test` (they should, same as any branch leaf).
3. **Decommission timing** — tear down `beta`/`alpha` immediately after `test` is verified, or keep them briefly in parallel? Default: tear down once `test` is green.
