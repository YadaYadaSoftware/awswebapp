## ADDED Requirements

### Requirement: A framework fix flows into a consumer by version bump

A change to the framework packages SHALL be adoptable by a downstream web application solely by changing the framework `PackageReference` version, restoring, building, and redeploying — with no copying of `Tjb` source.

#### Scenario: Sample adopts a framework change via version bump

- **WHEN** a representative observable change is published as a new `Tjb.Web.Framework`/`Tjb.Web.Hosting` version and the sample bumps its `PackageReference` to that version
- **THEN** after restore + build + redeploy, the change SHALL be observable in the deployed sample, with no edits to the sample beyond the version number

#### Scenario: Non-upgrading consumers are unaffected

- **WHEN** the framework publishes a new version and TaskManager has not bumped its reference
- **THEN** TaskManager's deployed behavior SHALL remain unchanged until it chooses to bump

### Requirement: A consumer can validate a fix from a pre-release before it lands on app

The framework SHALL publish branch-suffixed pre-release package versions for non-`app` branches, and a consumer SHALL be able to pin to such a pre-release to validate a fix on a feature branch before it reaches `app`.

#### Scenario: Consumer pins a branch-suffixed pre-release

- **WHEN** a fix is on a framework feature branch that publishes a branch-suffixed package version
- **THEN** a consumer SHALL be able to reference that pre-release version, build, and deploy to validate the fix prior to the fix merging to `app`

### Requirement: Versioning semantics and breaking-change policy are documented

The framework SHALL document its package versioning semantics (patch/minor/major meaning), the consumer upgrade runbook, and the breaking-change policy (what constitutes a breaking change, how it is signaled, and the deprecation overlap window).

#### Scenario: Upgrade runbook is documented and followable

- **WHEN** a consumer maintainer reads the upgrade documentation
- **THEN** it SHALL provide the concrete steps to adopt a framework version (bump, restore, build, test, deploy) and to consume a pre-release for validation

#### Scenario: Breaking changes are signaled

- **WHEN** the framework removes or renames an extension method, changes the Identity base shape, or changes a required configuration key
- **THEN** it SHALL bump the major version and record the change in the framework CHANGELOG with the migration guidance and deprecation overlap window

### Requirement: The slipstream loop is demonstrated end-to-end

The change SHALL demonstrate the full loop against the deployed sample — a representative framework change published, adopted by the sample via a version bump, redeployed, and observed — and record the demonstration.

#### Scenario: Recorded end-to-end demonstration

- **WHEN** the demonstration is performed
- **THEN** the representative change SHALL be shown published as a new version, adopted by the sample by bumping the version, redeployed, and observable in the deployed sample, and the run SHALL be recorded for reference
