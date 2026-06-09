## Context

`awswebapp` already split its deploy machinery into a reusable `workflow_call` (`deploy.yml`) + a
content NuGet package, and proved consumption with `src/sample` (the `sample-*` changes). The next
proof is a **standalone fork** (`awswebapp-sample`) that downloads everything and deploys. The fork
owner will delete `Tjb.*`, promote `src/sample` → `src/`, and deploy. This change prepares the source
repo so that path is guarded and documented.

Current relevant state (from the workflow inventory):
- `deploy.yml` (reusable) — fully parameterized; `workflow_call` already marks core secrets
  `required: true`. Docker build restores framework packages with `--build-arg GH_PACKAGES_TOKEN=${{ secrets.GITHUB_TOKEN }}`.
- `sample-deploy.yml` — thin caller of `deploy.yml`; gated by `vars.SAMPLE_DEPLOY_ENABLED`; reads
  `vars.DOMAIN_NAME`/`AWS_REGION_*` and passes a **hardcoded** `hosted-zone-id: Z06422172SASV44F5Y8VA`;
  build job sets `GH_PACKAGES_TOKEN: ${{ secrets.GITHUB_TOKEN }}`.
- `zbuild.yml` — TaskManager-specific (packs `Tjb.*`, publishes `src/Tjb.Web`).
- `cleanup-on-branch-delete.yml` — generic, domain-derived.
- `src/sample/nuget.config` — points at `https://nuget.pkg.github.com/YadaYadaSoftware/index.json`,
  maps `Tjb.*` to that feed, expands `%GH_PACKAGES_TOKEN%`. Framework pinned `1.1.0.190-dev`.
- Existing guard idioms to reuse: prod-credential empty-check and SSM "empty or missing" checks in
  `deploy.yml`; the `SAMPLE_DEPLOY_ENABLED` gate.

## Goals / Non-Goals

**Goals:**
- A fork hits clear, actionable failures (not cryptic deep ones) when config is missing.
- The cross-org framework-feed PAT path works in CI without breaking same-org TaskManager.
- A reader can follow the README to fork → configure → deploy without reading source.
- A fork knows which workflows to keep, adapt, and delete.

**Non-Goals:**
- Performing the fork, deleting `Tjb.*`, or promoting `src/sample` (the user's manual act).
- Restructuring or renaming anything in `awswebapp`; changing TaskManager's deploy behavior.
- Cutting a stable framework release (docs only note pinning the existing prerelease).
- Re-implementing the reusable `deploy.yml` internals — only its token-source surface.

## Decisions

### D1. The preflight guard lives in the caller (`sample-deploy.yml`), runs first, fails fast
A `validate-config` job runs before `build-sample`/`deploy`. It collects **all** missing required
config into one message and exits non-zero with a checklist (don't fail on the first miss — list them
all), each line naming the exact var/secret and where to set it (Settings → Secrets and variables →
Actions → Variables/Secrets). Required vars: `DOMAIN_NAME`, `AWS_REGION_PRIMARY`,
`AWS_REGION_SECONDARY`, `HOSTED_ZONE_ID`. Required secrets: `AWS_ACCESS_KEY_ID`,
`AWS_SECRET_ACCESS_KEY`, `DATABASE_PASSWORD`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`,
`FRAMEWORK_FEED_TOKEN`. Optional (warn only): `AWS_*_PROD`, `GOOGLE_TEST_*`. Secrets can't be read by
name to enumerate, so the job receives them via `env:`/`with:` and checks emptiness (the
`deploy.yml` prod-cred check is the pattern). It stays a no-op for TaskManager (all set).

**Why the caller, not `deploy.yml`:** `deploy.yml`'s `workflow_call required: true` already rejects a
call missing core secrets, but the *cryptic* failures are at the caller level — empty `vars.DOMAIN_NAME`
→ malformed names, missing feed PAT → restore 401 deep in the Docker build. The caller is where a fork
supplies repo config, so that's where the checklist belongs.

### D2. Framework-feed token is configurable, defaulting to `GITHUB_TOKEN`
Introduce a `FRAMEWORK_FEED_TOKEN` secret. In `sample-deploy.yml`, the build job's `GH_PACKAGES_TOKEN`
becomes `${{ secrets.FRAMEWORK_FEED_TOKEN || secrets.GITHUB_TOKEN }}` (caller-side, where the `||`
fallback on the `secrets` context is permitted). `deploy.yml` gains an **optional**
`framework-feed-token` secret; its Docker `--build-arg GH_PACKAGES_TOKEN` uses that secret when
provided, else `secrets.GITHUB_TOKEN`. Same-org TaskManager sets nothing new and keeps using
`GITHUB_TOKEN`; the cross-org fork sets `FRAMEWORK_FEED_TOKEN` to a YadaYada `read:packages` PAT.

**Why:** the workflow `GITHUB_TOKEN` is scoped to the running repo's org; it cannot read YadaYada's
packages from a different org's repo. This is the #1 fork failure mode.

### D3. Promote the hardcoded hosted zone to `vars.HOSTED_ZONE_ID`
`sample-deploy.yml` passes `hosted-zone-id: ${{ vars.HOSTED_ZONE_ID }}` instead of the literal. So a
fork supplies its own zone via config, not a code edit, and the preflight can validate it. TaskManager
gets a repo Variable `HOSTED_ZONE_ID = Z06422172SASV44F5Y8VA` so it doesn't regress (operator step,
documented). `deploy.yml`'s `hosted-zone-id` input is unchanged (still required).

### D4. The setup guide is the deliverable that ties it together
A root-`README.md` section "Setting up a new repo" carries: the keep/adapt/delete table, the
keep-vs-delete file list, the promotion steps (paths in `sample-deploy.yml` + the Dockerfile
`COPY/WORKDIR` + `Sample.sln`/ProjectReferences + `nuget.config`), the exact vars/secrets checklist
(mirroring the preflight, so docs == guard), a link to `src/sample/DEPLOYING.md` for AWS prereqs, the
framework PAT + version-pinning note, and troubleshooting mapping preflight errors → fixes.

### D5. Spec ownership
The new `standalone-fork-readiness` capability owns the *contract* (which config is required vs
optional, the fail-fast behavior, the documented onboarding path, the workflow classification). The
mechanism edits to `sample-deploy.yml`/`deploy.yml` are deltas on the existing `sample-ci-deployment`
and `reusable-deployment-stack` capabilities.

## Risks / Trade-offs

- **[Risk] Framework only exists as the moving `1.1.0.190-dev` prerelease.** A fork pinning it can
  break when it's superseded. → Docs instruct pinning an exact version; flag that a stable framework
  release is the durable fix (separate change).
- **[Risk] Can't truly test the "missing config" branches without unsetting TaskManager's prod
  config.** → Verify the all-present (no-op) path on TaskManager and reason through the missing
  branches; the fork is the real test of the failure messages.
- **[Trade-off] `secrets.A || secrets.B` in `with:`/`env:`** is allowed at the caller but not inside a
  reusable workflow's body — hence the split (caller does the fallback; `deploy.yml` takes an explicit
  optional secret). Slightly more surface, but keeps same-org behavior unchanged.
- **[Risk] Docs drift from the preflight.** → The vars/secrets checklist in the README must match the
  preflight's list exactly; a verification task diffs them.
