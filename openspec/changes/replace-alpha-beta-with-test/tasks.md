## 1. Deploy workflows

- [x] 1.1 `.github/workflows/zbuild.yml`: replace the `beta`/`alpha` branch test (`elif [[ "$BRANCH_NAME" = "beta" || "$BRANCH_NAME" = "alpha" ]]`) with the `test` equivalent, and update the deploy gate `contains(fromJson('["dev","alpha","beta","app"]'), …)` → `["dev","test","app"]`.
- [x] 1.2 `.github/workflows/deploy.yml`: change the `workflow_call` input defaults `"app beta alpha"` → `"app test"` (multi-region set) and `"app beta alpha dev"` → `"app test dev"` (shared set).
- [x] 1.3 `.github/workflows/cleanup-on-branch-delete.yml`: protected-branch guard `" app beta alpha dev "` → `" app test dev "`, and the summary "Reason" text listing `app`/`beta`/`alpha`/`dev` → `app`/`test`/`dev`.

## 2. Infrastructure templates

- [x] 2.1 `infrastructure/db.template`: the `EnvironmentToImport` condition enabling the Data API HTTP endpoint for `beta`/`alpha`/`dev` → `test`/`dev` (renamed `IsDevBetaOrAlpha` → `IsTestOrDev`, updated its `EnableHttpEndpoint` reference).
- [x] 2.2 `infrastructure/master.template`: updated the multi-region `Description` → "app, test branches"; removed the stale beta cutover-marker comment.
- [x] 2.3 `infrastructure/application.template`: updated the multi-region `Description` → "app, test branches".

## 3. Scripts / tooling

- [x] 3.1 `scripts/_BrothersCommon.ps1`: homestead `@('app','beta','alpha','dev')` → `@('app','test','dev')`.
- [x] 3.2 `scripts/Get-Brothers.ps1`: updated the homestead references in help/comments → `app/test/dev`.
- [x] 3.3 `scripts/merge.sh`: shared-branch guard `" app beta alpha dev "` → `" app test dev "`.

## 4. Application code & config

- [x] 4.1 Surveyed `src/**`: no env names in app config (appsettings/launch settings clean); the only `src/**` reference was a comment in `src/Tjb.Web/Tjb.Web.csproj` ("Homestead branches (dev/alpha/beta/app) …") → updated to `dev/test/app`.

## 5. GitVersion (decision D3)

- [x] 5.1 `GitVersion.yml`: renamed the `alpha`/`beta` prerelease `tag:` labels — `develop`→`dev`, `feature`→`feature`, `release`→`rc`, `hotfix`→`hotfix`. `main`(`app`) mapping unchanged. No `alpha`/`beta` token remains.

## 6. Docs

- [x] 6.1 `CLAUDE.md`: branch model, multi-region shared branch list, protected/homestead references → `app`/`test`/`dev` (and counts adjusted).
- [x] 6.2 `BROTHERS.md`: homestead diagram + description → `app`/`test`/`dev` (four → three).
- [x] 6.3 Additional docs caught by the §7.1 gate (sync-specs audit had rewritten these to the old app/beta/alpha model): `CONSUMING.md` (input defaults + the `beta|alpha) ENV=Staging` case → `test`), `README.md`, `BRANCH_MANAGEMENT_README.md`, and `src/docs/**` (ARCHITECTURE, AWS_DEPLOYMENT_GUIDE, AWS_DEPLOYMENT_SETUP, DATABASE_ACCESS_GUIDE, DEPLOYMENT_STRATEGY, GOOGLE_OAUTH_SETUP, IMPLEMENTATION_PLAN, WEB_DEPLOYMENT_STRATEGY). `report.md` left as a generated artifact.

## 7. Verify source is clean

- [x] 7.1 Grep gate: `\b(alpha|beta)\b` (case-insensitive) over `.github/**`, `infrastructure/**`, `scripts/**`, `src/**`, root config, and docs returns zero environment references (`openspec/` history, generated `report.md`, and unrelated words like "alphabet" excluded). ✓
- [x] 7.2 `openspec validate replace-alpha-beta-with-test --strict` passes. ✓

## 8. Rollout (decision D4/D5) — operator/deploy, NOT done in this implementation pass

- [ ] 8.1 Merge the source change to `dev`; confirm dev deploy stays green.
- [ ] 8.2 Create the `test` branch from `dev`; confirm it deploys a multi-region `test` env (both regions, Aurora Global Cluster, nonprod KMS) and `https://test.{DOMAIN_NAME}` is healthy.
- [ ] 8.3 Decommission `beta`: delete the secondary-region stack, then the primary-region stack (the global-cluster-aware teardown Lambda drains/detaches automatically); confirm both `DELETE_COMPLETE` and the global cluster is gone.
- [ ] 8.4 Decommission `alpha`: same sequence as 8.3.
- [ ] 8.5 Delete the `beta` and `alpha` branches.
- [ ] 8.6 Promote the change to `app`.
- [ ] 8.7 Post-rollout: re-run the §7.1 grep gate against `app` and confirm no `alpha`/`beta` environment remains anywhere.
