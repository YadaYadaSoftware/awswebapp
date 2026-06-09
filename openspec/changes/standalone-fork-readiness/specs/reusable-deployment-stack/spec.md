## ADDED Requirements

### Requirement: Reusable workflow accepts an optional framework-feed token

The reusable deploy workflow (`deploy.yml`) SHALL declare an **optional** `framework-feed-token`
secret and use it as the `GH_PACKAGES_TOKEN` build-arg for the container build (which restores the
web app's framework packages). When `framework-feed-token` is not provided, the workflow SHALL fall
back to the automatic `GITHUB_TOKEN`, so same-organization callers are unaffected. This lets a
cross-organization consumer supply a `read:packages` PAT for a framework feed in a different
organization without changing any other input.

#### Scenario: Token provided by a cross-org caller
- **WHEN** a caller invokes `deploy.yml` and passes the `framework-feed-token` secret
- **THEN** the Docker build receives that token as `GH_PACKAGES_TOKEN` and restores the framework packages from the cross-org feed

#### Scenario: Token omitted by a same-org caller
- **WHEN** a caller invokes `deploy.yml` without `framework-feed-token`
- **THEN** the Docker build uses the workflow `GITHUB_TOKEN`, preserving existing same-org behavior
