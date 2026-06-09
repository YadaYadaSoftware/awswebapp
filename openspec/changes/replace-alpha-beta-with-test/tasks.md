## 1. Deploy workflows

- [ ] 1.1 `.github/workflows/zbuild.yml`: replace the `beta`/`alpha` branch test (`elif [[ "$BRANCH_NAME" = "beta" || "$BRANCH_NAME" = "alpha" ]]`) with the `test` equivalent, and update the deploy gate `contains(fromJson('["dev","alpha","beta","app"]'), …)` → `["dev","test","app"]`.
- [ ] 1.2 `.github/workflows/deploy.yml`: change the `workflow_call` input defaults `"app beta alpha"` → `"app test"` (multi-region set) and `"app beta alpha dev"` → `"app test dev"` (shared set).
- [ ] 1.3 `.github/workflows/cleanup-on-branch-delete.yml`: protected-branch guard `" app beta alpha dev "` → `" app test dev "`, and the summary "Reason" text listing `app`/`beta`/`alpha`/`dev` → `app`/`test`/`dev`.

## 2. Infrastructure templates

- [ ] 2.1 `infrastructure/db.template`: the `EnvironmentToImport` condition enabling the Data API HTTP endpoint for `beta`/`alpha`/`dev` → `test`/`dev` (drop the `beta` and `alpha` `!Equals` terms, add `test`).
- [ ] 2.2 `infrastructure/master.template`: update the multi-region `Description` ("app, beta, alpha branches") → "app, test branches"; remove the stale beta cutover-marker comment if present.
- [ ] 2.3 `infrastructure/application.template`: update the multi-region `Description` ("app, beta, alpha branches") → "app, test branches".

## 3. Scripts / tooling

- [ ] 3.1 `scripts/_BrothersCommon.ps1`: homestead `@('app','beta','alpha','dev')` → `@('app','test','dev')`.
- [ ] 3.2 `scripts/Get-Brothers.ps1`: update the homestead references in help/comments (`app/beta/alpha/dev` → `app/test/dev`).
- [ ] 3.3 `scripts/merge.sh`: shared-branch guard `" app beta alpha dev "` → `" app test dev "`.

## 4. Application code & config

- [ ] 4.1 Grep the `src/**` tree for any `alpha`/`beta` environment names in app config (`appsettings*.json`, environment switches, launch settings) and replace/remove. (Survey showed infra/workflow/scripts as the carriers; confirm app code is clean.)

## 5. GitVersion (decision D3)

- [ ] 5.1 `GitVersion.yml`: rename the `alpha`/`beta` prerelease `tag:` labels on the `release`/`hotfix` (and any support) branches so no `alpha`/`beta` token remains (e.g. `rc`/`hotfix`). Leave `main`(`app`)/`develop`(`dev`) mappings intact. (If the user opts to keep semver labels out of scope, skip and note it.)

## 6. Docs

- [ ] 6.1 `CLAUDE.md`: branch model, the multi-region shared branch list (`app`/`beta`/`alpha`), and any homestead references → `app`/`test`/`dev`.
- [ ] 6.2 `BROTHERS.md`: homestead = `app`/`beta`/`alpha`/`dev` → `app`/`test`/`dev`.

## 7. Verify source is clean

- [ ] 7.1 Grep gate: `\b(alpha|beta)\b` (case-insensitive) over `.github/**`, `infrastructure/**`, `scripts/**`, `src/**`, and root config returns zero environment references (GitVersion labels renamed per 5.1; `openspec/` history and unrelated words like "alphabet" excluded).
- [ ] 7.2 `openspec validate replace-alpha-beta-with-test --strict` passes.

## 8. Rollout (decision D4/D5)

- [ ] 8.1 Merge the source change to `dev`; confirm dev deploy stays green.
- [ ] 8.2 Create the `test` branch from `dev`; confirm it deploys a multi-region `test` env (both regions, Aurora Global Cluster, nonprod KMS) and `https://test.{DOMAIN_NAME}` is healthy.
- [ ] 8.3 Decommission `beta`: delete the secondary-region stack, then the primary-region stack (the global-cluster-aware teardown Lambda drains/detaches automatically); confirm both `DELETE_COMPLETE` and the global cluster is gone.
- [ ] 8.4 Decommission `alpha`: same sequence as 8.3.
- [ ] 8.5 Delete the `beta` and `alpha` branches.
- [ ] 8.6 Promote the change to `app`.
- [ ] 8.7 Post-rollout: re-run the §7.1 grep gate against `app` and confirm no `alpha`/`beta` environment remains anywhere.
