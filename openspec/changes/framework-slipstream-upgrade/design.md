## Context

By the time this change runs, the framework is packaged (`extract-web-framework-package`), a sample consumes it (`sample-solution-local`), and the sample is deployed (`sample-ci-deploy`). What remains unproven is the *upgrade* direction: a fix made in `Tjb` reaching the sample's running deployment by a version bump alone. This change makes that loop explicit, documented, and demonstrated. It is mostly process + documentation, with one concrete end-to-end demonstration.

## Goals / Non-Goals

**Goals:**
- Demonstrate, end-to-end and recorded, a framework change flowing into the deployed sample via a single version bump.
- Document the versioning semantics, the consumer upgrade runbook, pre-release validation, and the breaking-change policy.
- Confirm non-upgrading consumers (TaskManager) are unaffected until they opt in.

**Non-Goals:**
- Automating version bumps (Dependabot/Renovate) — manual first; automation is a follow-up.
- Changing the framework package shape or the deployment stack.

## Decisions

### D1. Use a small, observable change as the demonstration vehicle

Pick a representative framework change that is unambiguously visible in the deployed app (e.g. a copy/string tweak in the shared layout, or a small behavior in a hosting extension) — large enough to be observable, small enough to be reversible. Publish it as a new framework version; bump the sample's `PackageReference`; redeploy; observe. The change is chosen so success/failure is visually obvious in the deployed sample and trivially revertible.

### D2. Reuse the existing GitVersion + branch-suffix conventions

The framework packages already version via GitVersion with branch-suffixed pre-releases for non-`app` branches (from `extract-web-framework-package`). The slipstream contract documents *consumer-facing* meaning over that existing mechanism rather than inventing a new scheme: `app` builds → stable versions; feature branches → `-{branch}` pre-releases a consumer can pin to validate a fix early. The breaking-change policy layers SemVer intent (major = removed/renamed extension, changed Identity base, changed required config key) on top.

### D3. Documentation home

Put the contract where consumers already look. If `make-deployment-stack-reusable` shipped a `CONSUMING.md`, add a "Framework package upgrades" section there for symmetry with the deployment-stack upgrade story; otherwise a standalone `docs/framework-upgrades.md`. Add a `CHANGELOG.md` (or adopt the repo's existing changelog convention) for the framework packages.

## Risks / Trade-offs

- **[Risk] The demonstration deploys to a real environment to be observable.** → Use a cheap sample feature-branch deploy for the demonstration, not a shared-infrastructure branch; revert the representative change after recording.
- **[Risk] The "fix flows in by version bump alone" claim fails because the sample needed a code edit too.** → That would reveal a real coupling gap in `extract-web-framework-package`; treat a required sample code edit as a finding to fix upstream, not to paper over here.
- **[Trade-off] Documentation can drift from the actual versioning behavior.** → Keep the contract doc minimal and point at the single source of truth (the CI versioning step) rather than restating it.

## Open Questions

1. **What is the representative demonstration change?** A visible layout string is the safest (observable, reversible). Confirm before running the demonstration.
2. **Where does the upgrade contract live** — extend `CONSUMING.md` (if it exists) vs a new `docs/framework-upgrades.md`. Decide once `make-deployment-stack-reusable`'s doc surface is known.
