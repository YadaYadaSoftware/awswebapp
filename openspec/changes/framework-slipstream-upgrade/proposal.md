## Why

The point of packaging the framework was not just reuse but **slipstreaming**: fix a bug or add a feature in `Tjb`, and have it flow into every web app built on the framework via a version bump — without re-copying source. The first three changes build and deploy a second app; this one proves the upgrade loop actually works and defines the contract for it. Until a `Tjb.Web.Framework` change is shown landing in the deployed sample by changing one version number, "slipstream into existing applications" is an unproven claim.

Fourth and final of the coordinated set: `extract-web-framework-package` → `sample-solution-local` → `sample-ci-deploy` → **`framework-slipstream-upgrade`** (this).

## What Changes

- **A documented, demonstrated upgrade loop**: a representative change to `Tjb.Web.Framework`/`Tjb.Web.Hosting` (a small, observable fix or feature) is published as a new package version; the sample picks it up by bumping its `PackageReference` version; the sample redeploys and the change is observable in the deployed sample — with TaskManager unaffected until it bumps too.
- **The application-package versioning contract** — how `Tjb.Web.Framework`/`Tjb.Web.Hosting` versions map to consumer expectations: what a patch/minor/major bump means for a consumer, how a consumer pins (`@version`, floating, branch-suffixed pre-releases for testing a fix before it lands on `app`), and the compatibility guarantees. This parallels the deployment-package versioning contract `make-deployment-stack-reusable` defines for templates/workflow.
- **A consumer upgrade runbook** — the concrete steps a downstream app follows to take a framework fix: bump version, restore, build, test, deploy; plus how to consume a *pre-release* (branch-suffixed) package to validate a fix on a feature branch before it reaches `app`.
- **A breaking-change policy** for the framework packages — what constitutes a breaking change (removed/renamed extension method, changed Identity base shape, changed required config keys), how it's signaled (major bump + CHANGELOG), and the deprecation overlap window.

## Capabilities

### New Capabilities

- `framework-slipstream`: The contract and demonstrated workflow by which a fix or feature in the framework packages flows into a downstream web application via a package-version bump and redeploy, including the versioning semantics, the consumer upgrade runbook, pre-release validation, and the breaking-change policy. Demonstrated end-to-end against the deployed sample.

### Modified Capabilities

_None._ This change documents and demonstrates a workflow over the packages produced by `extract-web-framework-package`; it does not change those packages' requirements.

## Impact

**New artifacts:**
- A versioning/upgrade contract document (e.g. `docs/framework-upgrades.md` or a section in `CONSUMING.md` if that doc exists from `make-deployment-stack-reusable`) covering version semantics, the upgrade runbook, pre-release validation, and the breaking-change policy.
- A `CHANGELOG.md` (or established changelog convention) for `Tjb.Web.Framework`/`Tjb.Web.Hosting`.
- A demonstrated, recorded run of the loop (the representative fix + the sample version bump + the redeploy showing the change).

**Depends on:**
- `extract-web-framework-package` (the packages + their versioning), `sample-solution-local` (the consumer), and `sample-ci-deploy` (a deployed sample to observe the change in).

**Explicitly out of scope:**
- Automating the version bump in consumers (e.g. Dependabot/Renovate) — the loop is documented and demonstrated manually first; automation is a possible follow-up.
- Changing the framework packages' shape or the deployment stack.
