# reusable-deployment-stack Specification

## Purpose
TBD - created by archiving change make-deployment-stack-reusable. Update Purpose after archive.
## Requirements
### Requirement: CloudFormation templates are packaged and published as a NuGet artifact

The `infrastructure/` templates in this repo SHALL be packaged into a single NuGet package (working name: `YadaYada.AwsWebApp.DeploymentStack`) and published to this repo's GitHub Packages NuGet feed (`nuget.pkg.github.com/${owner}/index.json`) by the same publish job that publishes the existing `Tjb.*` packages.

The package SHALL contain every file currently in the `infrastructure/` directory of this repo, organized under a known `contentFiles/any/any/infrastructure/` path inside the package so consumers can locate them deterministically after `dotnet restore`. The package version SHALL match the `GitVersion`-computed SemVer used for the rest of the repo's NuGet packages.

#### Scenario: Package publishes on merge to app
- **WHEN** a commit lands on the `app` branch
- **THEN** the existing NuGet publish job in CI publishes a new version of the deployment-stack NuGet package alongside the other `Tjb.*` packages

#### Scenario: Templates discoverable after consumer restore
- **WHEN** a consumer project runs `dotnet restore` against a package reference to this deployment-stack package
- **THEN** every template from `infrastructure/` is available under the resolved package's `contentFiles/any/any/infrastructure/` directory

#### Scenario: Branch-suffixed versions for pre-release
- **WHEN** the publish job runs on a non-`app` branch
- **THEN** the NuGet version carries the branch-suffix pattern already used by `Tjb.Shared` etc. (e.g., `1.4.0-dev`), allowing consumers to test pre-release versions before they ship on `app`

### Requirement: Deploy workflow is reusable via `workflow_call`

A reusable workflow SHALL exist at `.github/workflows/deploy.yml` with `on: workflow_call:` at the top. The workflow SHALL accept a documented set of inputs and secrets, and SHALL perform the deploy operations currently done by the `deploy` job in `zbuild.yml`: build/push container image, look up bootstrap KMS keys, deploy CloudFormation templates, run UI tests against the deployed app.

The reusable workflow SHALL be callable from another repo as `uses: YadaYadaSoftware/awswebapp/.github/workflows/deploy.yml@<ref>`, where `<ref>` is a major version tag (`v1`), an exact tag (`v1.4.2`), or a branch name (`main`).

#### Scenario: Caller invokes the workflow from another repo
- **WHEN** a consumer repo's workflow contains a job with `uses: YadaYadaSoftware/awswebapp/.github/workflows/deploy.yml@v1` and provides all required inputs and secrets
- **THEN** the deploy workflow runs in the consumer's GitHub Actions context, building and deploying the consumer's application using its own AWS credentials

#### Scenario: TaskManager self-consumes
- **WHEN** the TaskManager repo's `zbuild.yml` calls `uses: ./.github/workflows/deploy.yml` after running its .NET build + test + NuGet publish jobs
- **THEN** the same reusable workflow runs the deploy steps for TaskManager itself, with TaskManager-specific inputs

#### Scenario: Versioning via tag
- **WHEN** the `app` branch is merged
- **THEN** the CI workflow tags the merge commit as `v<major>.<minor>.<patch>` (matching the NuGet SemVer), so consumers can pin to the tag

### Requirement: Input/secret surface is explicitly typed and documented

The reusable workflow SHALL declare every project-specific value as either an `input` (in the `on: workflow_call: inputs:` block) or a `secret` (in `on: workflow_call: secrets:`). The workflow SHALL fail loudly at run time if any required input or secret is missing.

The required inputs SHALL include at minimum: `branch-name`, `environment`, `custom-version`, `domain-name`, `hosted-zone-id`, `region-primary`, `region-secondary`, `multi-region-branches`, `shared-infra-branches`, `prod-branch`, `dotnet-version`, `web-dockerfile-path`. (Per the §0.1 derive-from-domain decision there is **no** `project-name` input — `domain-name` is the sole naming input.)

The required secrets SHALL include at minimum: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_ACCESS_KEY_ID_PROD`, `AWS_SECRET_ACCESS_KEY_PROD`, `DATABASE_PASSWORD`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`.

Every input SHALL have a sensible default OR be marked `required: true`. The README/`CONSUMING.md` SHALL document each input's purpose, type, default, and example value.

#### Scenario: Required input missing
- **WHEN** a consumer calls the reusable workflow without providing the `domain-name` input
- **THEN** GitHub Actions rejects the workflow at parse time, before any job runs, with a clear error naming the missing input

#### Scenario: Required secret missing
- **WHEN** a consumer calls the workflow with all inputs but without setting the `AWS_ACCESS_KEY_ID` secret
- **THEN** the existing "Select AWS credentials by branch" step's empty-secret guard fires and fails the workflow with a clear message identifying the missing secret

### Requirement: Templates derive all naming from the domain (no ProjectName axis)

Per the §0.1 design decision, every CloudFormation template SHALL derive project/resource naming from the deployment **domain** — the dashed form of `DomainName` (equivalently `${AWS::StackName}` for the bootstrap stack, whose name *is* the dashed domain) — rather than from a separate `ProjectName` parameter. No template SHALL introduce a `ProjectName` parameter, and the reusable workflow SHALL expose no `project-name` input; `domain-name` is the single naming input, passed through to each CFN deploy as `DomainName`.

No template SHALL contain the literal string `taskmanager`. No template SHALL contain a hardcoded domain in a resource *value* — including parameter `Default`s; the domain SHALL come from the `DomainName` parameter (or be derived from it, e.g. `!Sub "noreply@${DomainName}"`). Descriptions, examples, and comments MAY still mention a domain for documentation.

The IAM policies that scope to project-named resource ARNs SHALL scope to the dashed-domain-derived form (e.g. `!Sub "${AWS::StackName}-*"` in the bootstrap stack), not a hardcoded project literal.

#### Scenario: No hardcoded taskmanager strings
- **WHEN** `grep -r 'taskmanager' infrastructure/` is run after the change
- **THEN** no matches are returned (other than possibly in YAML comments documenting the rename)

#### Scenario: No hardcoded domain in resource values
- **WHEN** the templates are inspected for the literal `appcloud.systems`
- **THEN** it appears only in parameter descriptions, examples, or comments — never in a resource value or a parameter `Default` (the SES sender, for instance, is `!Sub "noreply@${DomainName}"`)

#### Scenario: Naming derives from the domain
- **WHEN** every template file in the package is inspected
- **THEN** each template that names project-scoped resources derives those names from `DomainName` (or `${AWS::StackName}`); leaf templates with no project-named resources need no naming parameter

#### Scenario: TaskManager passes its domain
- **WHEN** TaskManager's caller workflow invokes the reusable deploy workflow
- **THEN** `domain-name: appcloud.systems` is among the inputs, and the resulting CFN deploys carry `DomainName=appcloud.systems` as a parameter override, from which the dashed form `appcloud-systems` is derived inside the templates

### Requirement: Onboarding documentation exists and covers the full lift

A `CONSUMING.md` file SHALL exist at the repo root that documents, for a new .NET web-app project, exactly what they need to do to onboard. The document SHALL cover at minimum:

- Prereqs: AWS account, GitHub repo, .NET project layout assumptions
- Step-by-step onboarding: install the NuGet package, configure secrets, deploy the bootstrap stack manually, create the workflow file calling the reusable workflow
- Input reference: every input the workflow takes, with description, type, default, and example
- Secret reference: every secret with purpose
- Branch model: which branches deploy where, prod-vs-nonprod credentials, feature-branch isolation
- Troubleshooting: common errors and their fixes

#### Scenario: New consumer can self-serve
- **WHEN** a new .NET web-app team reads `CONSUMING.md` cold (no prior context on this project) and follows it end-to-end
- **THEN** they can complete onboarding without needing to read this repo's source code

#### Scenario: Documentation matches reality
- **WHEN** the inputs documented in `CONSUMING.md` are diffed against the actual `inputs:` block in `.github/workflows/deploy.yml`
- **THEN** the lists match exactly (every documented input is in the workflow; every workflow input is documented)

