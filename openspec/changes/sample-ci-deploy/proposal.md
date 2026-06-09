## Why

A sample that only runs locally proves consumption but not the end goal: **a second multi-regional web application spun from the framework.** This change deploys the `src/sample` solution through CI to `dev`/`alpha`/`beta`/`app` using the reusable deployment stack, with its own CloudFormation stacks, its own subdomain, and UI tests against the deployed URL. Completing it means the org can stand up additional web apps on the same machinery — the payoff the whole program exists for.

Third of the coordinated set: `extract-web-framework-package` → `sample-solution-local` → **`sample-ci-deploy`** (this) → `framework-slipstream-upgrade`.

## What Changes

- **A deploy workflow for the sample** that calls the reusable deploy workflow from `make-deployment-stack-reusable` (`uses: YadaYadaSoftware/awswebapp/.github/workflows/deploy.yml@<ref>` or the in-repo path form) with the sample's inputs: `project-name` = the sample's leaf (e.g. `sample-app`/agreed `ProjectName`), `domain-name`/`hosted-zone-id`, `web-project-path` = `src/sample/Sample.Web`, the sample's Dockerfile path, and the sample's `.NET` build/test/migrations wiring. The reusable workflow extracts the CloudFormation templates from the `YadaYada.AwsWebApp.DeploymentStack` package and deploys.
- **Sample CI build/test** — `dotnet build`/`dotnet test` for `Sample.sln` (restoring the framework + `Tjb.Shared` from GitHub Packages), and the sample's migrations applied on container startup as `Tjb.Web` does.
- **Branch/stack mapping for the sample** — the sample deploys to its own per-branch CloudFormation stacks following the established `{branch-leaf}-{processed-domain}` convention, multi-region on the shared-infrastructure branches and single-region elsewhere, consuming `dev`'s backend exports for feature branches exactly as TaskManager does. (Where the sample's stacks live relative to TaskManager's — same domain/different `ProjectName`, or a distinct domain — is settled in design.)
- **Sample UI tests against the deployed URL** — a `Sample.UiTests` (Playwright/xUnit) run against `https://<sample-leaf>.{DOMAIN_NAME}`, wired into the deploy workflow the same way `Tjb.UiTests` is, with TRX → `dorny/test-reporter` summary and artifact upload.
- **Resource tagging + cleanup parity** — the sample's stacks carry the six stack-level tags and are torn down by the branch-delete cleanup workflow, inheriting both behaviors from the reusable stack with no sample-specific code.

## Capabilities

### New Capabilities

- `sample-ci-deployment`: How the `src/sample` solution is built, tested, and deployed through CI to `dev`/`alpha`/`beta`/`app` (multi-region on shared-infrastructure branches) by consuming the reusable deployment stack, with its own subdomain, CloudFormation stacks, resource tags, branch-delete cleanup, and deployed-URL UI tests — proving a second web application can be spun from the framework.

### Modified Capabilities

_None._ This change adds a second deployable consumer; it relies on (does not redefine) the `reusable-deployment-stack` capability from `make-deployment-stack-reusable` and the `sample-consumer-solution` capability from `sample-solution-local`.

## Impact

**New artifacts:**
- A CI workflow for the sample (in this repo or a clearly-scoped job) that calls the reusable deploy workflow with the sample's inputs/secrets.
- `src/sample/Sample.UiTests/` (or completion of the optional smoke from `sample-solution-local`) targeting the deployed sample URL.
- Sample container/image naming + ECR usage per the reusable stack's conventions.

**Depends on (hard):**
- `make-deployment-stack-reusable` (wilhelm) — the reusable `deploy.yml` workflow and the parameterized templates / `YadaYada.AwsWebApp.DeploymentStack` package MUST be landed and consumable. The reusable workflow's required input surface (`project-name`, `domain-name`, `hosted-zone-id`, `web-project-path`, Dockerfile path, secondary region, multi-region branch list, secrets) is the contract this change fills.
- `sample-solution-local` — a buildable, NuGet-consuming sample solution with a Dockerfile and migrations runner.

**External / operational:**
- AWS prerequisites for the sample's domain: a Route 53 hosted zone, ACM/DNS, and (per the reusable stack's account model) a bootstrap stack and the SES per-region identity setup if the sample sends email. These manual prerequisites are documented, not automated, in this change.

**Explicitly out of scope:**
- Building the reusable deployment stack itself (that is `make-deployment-stack-reusable`).
- The bug-fix slipstream demonstration (that is `framework-slipstream-upgrade`).
- Any new infrastructure capability — the sample uses the reusable stack as-is; if it can't, the gap is reported back to `make-deployment-stack-reusable` rather than patched here.
