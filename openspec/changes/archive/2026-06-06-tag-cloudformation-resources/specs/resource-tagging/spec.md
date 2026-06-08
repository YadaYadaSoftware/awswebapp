## ADDED Requirements

### Requirement: Canonical resource tag set

Every CloudFormation deploy SHALL apply the following stack-level tags to the deployed stack: `Stack Name`, `Create Date`, `Branch`, `Specification`, `Version`, and `DeployRunUrl`. These tags SHALL be applied through CloudFormation stack-level tagging so that they propagate to every taggable resource in the stack and into nested stacks, rather than being declared per-resource in the templates.

#### Scenario: Tags present on a deployed stack

- **WHEN** a deploy of any branch completes
- **THEN** `aws cloudformation describe-stacks` for that stack returns all six tag keys (`Stack Name`, `Create Date`, `Branch`, `Specification`, `Version`, `DeployRunUrl`) with non-empty values

#### Scenario: Tags propagate to nested-stack resources

- **WHEN** a master-template deploy (`dev`/`app`/`beta`/`alpha`) completes
- **THEN** a taggable resource created inside the nested `backend` or `application` stack (e.g. the Aurora cluster, ECS cluster, or load balancer) carries the same six tag keys with values matching the parent stack

#### Scenario: New resources are covered automatically

- **WHEN** a new taggable resource type is added to any template
- **THEN** it receives the six tags on the next deploy with no change to the tagging mechanism, because tags are applied at the stack level rather than per resource

### Requirement: Stack Name tag value

The `Stack Name` tag SHALL equal the CloudFormation stack name, computed as `${branch-leaf}-${processed-domain}` (the same value the deploy job uses to name the stack).

#### Scenario: Stack Name matches the deployed stack name

- **WHEN** the `dev` branch deploys against domain `appcloud.systems`
- **THEN** the `Stack Name` tag value is `dev-appcloud-systems`, equal to the stack's own name

### Requirement: Create Date tag preserves first-deploy date

The `Create Date` tag SHALL record the UTC date the stack was first created and SHALL remain unchanged across subsequent deploys. Before each deploy the workflow SHALL read the existing stack's `Create Date` tag; if present it SHALL be reused, otherwise the current UTC date SHALL be used. The absence of the stack (first deploy) SHALL be handled without failing the deploy.

#### Scenario: First deploy stamps today

- **WHEN** a stack is deployed for the first time (no existing stack)
- **THEN** the `Create Date` tag is set to the current UTC date and the deploy proceeds without error from the lookback

#### Scenario: Redeploy preserves the original date

- **WHEN** a stack that already carries a `Create Date` tag is redeployed on a later day
- **THEN** the `Create Date` tag value is unchanged from its original value

### Requirement: Branch tag value

The `Branch` tag SHALL equal the git branch leaf (the segment after the last `/`), as already computed by the deploy workflow.

#### Scenario: Branch leaf is used

- **WHEN** a push to branch `feature/some-thing` triggers a deploy
- **THEN** the `Branch` tag value is `some-thing`

### Requirement: Specification tag value

The `Specification` tag SHALL identify the OpenSpec change a deploy belongs to. For non-shared branches it SHALL equal the branch leaf (which, by repository convention, equals the OpenSpec change name). For the shared-infrastructure branches `app`, `beta`, `alpha`, and `dev` it SHALL be the fixed sentinel value `shared-infrastructure`.

#### Scenario: Feature branch uses its spec name

- **WHEN** branch `tag-cloudformation-resources` deploys
- **THEN** the `Specification` tag value is `tag-cloudformation-resources`

#### Scenario: Shared branch uses the sentinel

- **WHEN** branch `dev` (or `app`/`beta`/`alpha`) deploys
- **THEN** the `Specification` tag value is `shared-infrastructure`

### Requirement: Version tag value

The `Version` tag SHALL equal the build's computed version (the workflow's SemVer/`customVersion`), so a resource can be traced to the build that deployed it.

#### Scenario: Version reflects the build

- **WHEN** a deploy runs for build version `1.2.3.45`
- **THEN** the `Version` tag value is `1.2.3.45`

### Requirement: DeployRunUrl tag value

The `DeployRunUrl` tag SHALL be the URL of the GitHub Actions run that performed the deploy, composed from the run's server URL, repository, and run id. Unlike `Create Date`, this value SHALL be updated to the current run on every deploy.

#### Scenario: DeployRunUrl points at the deploying run

- **WHEN** a deploy runs in GitHub Actions run `12345`
- **THEN** the `DeployRunUrl` tag value is `${server_url}/${repository}/actions/runs/12345`

### Requirement: Domain- and region-agnostic tag computation

Tag values SHALL be derived from existing workflow context (branch leaf, processed domain, version, GitHub run context) and SHALL NOT hardcode any specific domain, branch, or region. Tag keys containing spaces (`Stack Name`, `Create Date`) SHALL be passed with correct JSON quoting so they are applied verbatim.

#### Scenario: No hardcoded domain or region

- **WHEN** the tagging logic is applied to a different domain or region
- **THEN** the tag values reflect that domain/region/branch without any change to the workflow source

#### Scenario: Spaced keys applied verbatim

- **WHEN** the tags are applied
- **THEN** the resulting AWS tag keys are exactly `Stack Name` and `Create Date` (with the space), not a mangled or split form
