## 1. Prereqs and reconciliation

- [ ] 1.1 Confirm the exact `deploy.yml` input/secret surface and the `sample-deploy.yml` token/zone wiring still match this design (re-grep `secrets.GITHUB_TOKEN`, `vars.*`, `hosted-zone-id`); note any drift since proposal.
- [ ] 1.2 Decide the `FRAMEWORK_FEED_TOKEN` fallback mechanism and verify `secrets.FRAMEWORK_FEED_TOKEN || secrets.GITHUB_TOKEN` is valid in a caller `env:`/`with:` (it is at the caller level, not inside a reusable workflow body).

## 2. Framework-feed token parameterization

- [ ] 2.1 `deploy.yml`: add an **optional** `framework-feed-token` secret to the `workflow_call.secrets` block; change the container build's `--build-arg GH_PACKAGES_TOKEN=...` to use it when set, else `secrets.GITHUB_TOKEN`.
- [ ] 2.2 `sample-deploy.yml`: set the build job's `GH_PACKAGES_TOKEN` to `${{ secrets.FRAMEWORK_FEED_TOKEN || secrets.GITHUB_TOKEN }}` and pass `framework-feed-token: ${{ secrets.FRAMEWORK_FEED_TOKEN }}` into the `deploy.yml` call (harmless empty for same-org TaskManager).
- [ ] 2.3 Validate both workflows with `npx --yes js-yaml`.

## 3. Hosted-zone variable

- [ ] 3.1 `sample-deploy.yml`: replace the literal `hosted-zone-id: Z06422172SASV44F5Y8VA` with `${{ vars.HOSTED_ZONE_ID }}`.
- [ ] 3.2 **(operator)** add TaskManager repo Variable `HOSTED_ZONE_ID = Z06422172SASV44F5Y8VA` so TaskManager's sample-deploy path doesn't regress; document in the README/CLAUDE.md.

## 4. Config-validation preflight

- [ ] 4.1 Add a `validate-config` job to `sample-deploy.yml` that runs before `build-sample`/`deploy`, receives the required secrets via `env:`, and collects **all** missing required variables (`DOMAIN_NAME`, `AWS_REGION_PRIMARY`, `AWS_REGION_SECONDARY`, `HOSTED_ZONE_ID`) and secrets (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `DATABASE_PASSWORD`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `FRAMEWORK_FEED_TOKEN`) into one message, exiting non-zero if any are missing. Print where to set each (Settings → Secrets and variables → Actions).
- [ ] 4.2 Warn (don't fail) on missing optional config: `AWS_ACCESS_KEY_ID_PROD`/`_SECRET_…_PROD`, `GOOGLE_TEST_ACCESS_TOKEN`/`_REFRESH_TOKEN`. Gate `build-sample`/`deploy` on `validate-config` succeeding.
- [ ] 4.3 Keep it a no-op for TaskManager (all config present) and ensure it still respects the `SAMPLE_DEPLOY_ENABLED` gate. Re-validate YAML.

## 5. "Setting up a new repo" README section

- [ ] 5.1 Add a "Setting up a new repo" section to root `README.md` after "Also a reusable deployment library": fork → **keep vs delete** file list → promote `src/sample` → `src/` (paths in `sample-deploy.yml`, the `Sample.Web/Dockerfile` `COPY/WORKDIR`, `Sample.sln`, ProjectReferences, `nuget.config`).
- [ ] 5.2 Document the framework feed: point `nuget.config` at YadaYada's feed, **pin an exact framework version** (not the moving `1.1.0.190-dev`), create a `read:packages` PAT, set it as `FRAMEWORK_FEED_TOKEN`.
- [ ] 5.3 Add the **required/optional variables + secrets checklist** — must match the preflight's lists exactly (verified in 7.2). Link `src/sample/DEPLOYING.md` for AWS prerequisites (bootstrap per region, hosted zone, dev Aurora, SES).
- [ ] 5.4 Add the **workflow keep/adapt/delete inventory** table (`deploy.yml` KEEP, `cleanup-on-branch-delete.yml` KEEP, `sample-deploy.yml` ADAPT, `zbuild.yml` DELETE/replace) and a **troubleshooting** subsection mapping each preflight error to its fix.
- [ ] 5.5 `CLAUDE.md`: one-line pointer to the new README section; note `HOSTED_ZONE_ID` is now a repo Variable. Light note in `src/sample/README.md`/`DEPLOYING.md` about the cross-org `FRAMEWORK_FEED_TOKEN`.

## 6. No-regression check for TaskManager

- [ ] 6.1 Confirm the changes are additive for TaskManager: with `HOSTED_ZONE_ID` set and no `FRAMEWORK_FEED_TOKEN`, `sample-deploy.yml` + `deploy.yml` behave exactly as before (preflight passes, token falls back to `GITHUB_TOKEN`). A push of the gated sample workflow should be a green no-op.

## 7. Docs, validation, archive

- [ ] 7.1 `openspec validate standalone-fork-readiness --strict` and resolve issues.
- [ ] 7.2 Diff the README config checklist against the preflight's required/optional lists — they must match exactly (the "docs match the guard" scenario).
- [ ] 7.3 Verify each scenario in `specs/standalone-fork-readiness/spec.md` and `specs/reusable-deployment-stack/spec.md` to the extent possible without an actual fork (the cross-org / missing-config paths are proven by the eventual fork, out of band).
- [ ] 7.4 Archive this change (`/opsx:archive`) once merged to `dev` and validated.
