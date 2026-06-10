## Why

The `awswebapp` repo will be **forked into a new standalone repo (`awswebapp-sample`)** to prove the
framework + reusable deploy stack can be downloaded and deployed correctly by a third party. The fork
owner will, by hand in the new repo, delete all `Tjb.*` code, promote `src/sample` → `src/`, and
deploy. Today that path has sharp edges: it's unclear which workflows survive a fork, the workflows
fail deep and cryptically when a required secret/variable is unset, and there's no onboarding guide.
This change hardens `awswebapp` *before* the fork so the fork has the best chance of first-try success.

A key constraint shapes everything: the fork **consumes the framework as NuGet packages
(`Tjb.Web.Framework`/`Hosting`/`Framework.Data`) from YadaYadaSoftware's GitHub Packages feed**, not
as kept source. That makes the consumption **cross-org**, so the workflow's `GITHUB_TOKEN` (which only
reads the *fork's own* org packages) is insufficient — the fork needs a `read:packages` PAT. This is
the single most likely thing to silently break a fork, so it must be both guarded and documented.

## What Changes

This change is **prep-only in `awswebapp`** — it does **not** restructure this repo or break
TaskManager. It adds:

- **Config-validation guards** to the deploy caller workflow (`sample-deploy.yml`): a fast-failing
  preflight that lists every missing required repo **variable** (`DOMAIN_NAME`, `AWS_REGION_PRIMARY`,
  `AWS_REGION_SECONDARY`, `HOSTED_ZONE_ID`) and **secret** (`AWS_ACCESS_KEY_ID`,
  `AWS_SECRET_ACCESS_KEY`, `DATABASE_PASSWORD`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`,
  `FRAMEWORK_FEED_TOKEN`) with exactly what to set and where, before any build/deploy. Optional config
  (prod creds, `GOOGLE_TEST_*`) is warned, not failed. Reuses the existing guard idioms (the
  prod-credential empty-check and SSM "empty or missing" checks in `deploy.yml`).
- **Framework-feed-token parameterization** so the cross-org PAT works without breaking same-org
  TaskManager: `sample-deploy.yml`/`deploy.yml` source the GitHub Packages token from a configurable
  `FRAMEWORK_FEED_TOKEN` that **defaults to `GITHUB_TOKEN`**; the hardcoded `hosted-zone-id` literal
  becomes `vars.HOSTED_ZONE_ID` (added to TaskManager so it keeps working).
- A **"Setting up a new repo" guide** in the root `README.md`: fork → keep-vs-delete → promote
  `src/sample` → `src/` → `nuget.config` + the framework PAT (pin an exact version, not the moving
  `-dev`) → the vars/secrets checklist → AWS prerequisites (links `src/sample/DEPLOYING.md`) → first
  build/test/deploy → troubleshooting that maps each preflight error to its fix.
- A **workflow keep/adapt/delete inventory**: `deploy.yml` **KEEP**, `cleanup-on-branch-delete.yml`
  **KEEP**, `sample-deploy.yml` **ADAPT** (becomes the fork's main deploy + holds the preflight),
  `zbuild.yml` **DELETE/replace** (TaskManager-specific: packs `Tjb.*`, publishes `src/Tjb.Web`).

The actual fork/delete/promote/deploy is the user's manual act in the new repo — **out of scope here**.

## Capabilities

### New Capabilities

- `standalone-fork-readiness`: How `awswebapp` is made fork-ready — the config-validation preflight
  contract (which vars/secrets are required vs optional and the fail-fast behavior), the documented
  "set up a new repo" onboarding path, and the workflow keep/adapt/delete classification that tells a
  fork what to reuse.

- `reusable-deployment-stack`: `deploy.yml` accepts an optional `framework-feed-token` secret used for
  the Docker `--build-arg GH_PACKAGES_TOKEN`, so a cross-org consumer can restore the framework
  packages (defaults to `GITHUB_TOKEN` for same-org callers).

> The sample-deploy-workflow changes (preflight guard, `FRAMEWORK_FEED_TOKEN` fallback,
> `vars.HOSTED_ZONE_ID`) are owned by the **new** `standalone-fork-readiness` capability below rather
> than declared as a modification to `sample-ci-deployment`, whose spec isn't synced yet (its change
> is still active).

## Impact

- **Workflows:** `.github/workflows/sample-deploy.yml` (preflight job + token/zone parameterization),
  `.github/workflows/deploy.yml` (optional `framework-feed-token` secret).
- **Docs:** root `README.md` (new section + inventory table), `CLAUDE.md` (pointer + note that
  `HOSTED_ZONE_ID` is now a repo Variable), a "Deploy via the GitHub pipeline (CI)" section in
  `src/sample/README.md` (which previously only covered local run), expanded step-by-step
  bootstrap-stack deployment instructions in `src/sample/DEPLOYING.md` (deploy command,
  primary→secondary KMS-ARN handoff, deploy order, and the `GitHubActionsUser*` outputs → `AWS_*`
  secrets mapping), and notes in `src/sample/README.md` / `DEPLOYING.md` about the cross-org
  `FRAMEWORK_FEED_TOKEN`.
- **Operator/config:** a new TaskManager repo **Variable** `HOSTED_ZONE_ID = Z06422172SASV44F5Y8VA`
  must be set so the zone parameterization doesn't regress TaskManager (manual, documented).
- **No source/app/template changes**; TaskManager's build + deploy behavior is unchanged.
- **Out of scope:** performing the fork; deleting `Tjb.*`; promoting `src/sample`; cutting a stable
  framework release (the docs note pinning the existing prerelease).
