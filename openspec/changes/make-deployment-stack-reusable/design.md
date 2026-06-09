## Context

The TaskManager project (this repo) has converged on a non-trivial AWS deployment pattern that took real time to figure out:

- Bootstrap stack with consolidated KMS + IAM + ECR + S3 bucket policy + Config rules
- Multi-region Aurora Serverless v2 Global Cluster with prod/nonprod KMS isolation
- Per-branch CloudFormation stacks for app/beta/alpha/dev (multi-region for the three prod-ish branches) and feature branches (single region, consume dev's backend exports)
- Branch-conditional CI credentials (separate `GitHubActionsUser` and `GitHubActionsUserProd`)
- SSM-based discovery for bootstrap-published values
- Branch-delete cleanup workflow with protected-branch guard
- Container build + ECR push with source-checksum tags
- Rolling Fargate deploys with health checks; DNS failover via Route 53

That pattern is not project-specific in any meaningful way. Another .NET web app with a similar shape could deploy on the same machinery, and would benefit hugely from doing so — they wouldn't have to discover (or re-implement) the same edge cases (KMS lockout-safety, RDS SLR semantics, NotPrincipal-vs-targeted-Deny, ApiGateway-singleton-per-region, etc.). But today it's impossible to reuse: the templates hardcode resource patterns like `taskmanager-*`, the workflow knows about specific file paths (`src/Tjb.Web/Dockerfile`), and there's no extracted contract for "what does a consumer project need to provide."

This change extracts the project-agnostic infrastructure into a reusable package.

## Goals / Non-Goals

**Goals:**

- TaskManager itself can be deployed by *consuming* its own packaged deploy stack rather than by carrying the templates inline. The lift-and-shift should be invisible to operators — same CI behavior, same stack outputs, same URLs.
- A new .NET web app can adopt this deploy stack by: installing one NuGet package, calling one reusable workflow with maybe 10 inputs, and pre-deploying one bootstrap stack manually. Onboarding measured in hours, not weeks.
- Updates to the deploy stack (bug fixes, new features, AWS API changes) ship as new NuGet versions. Consumers upgrade by bumping a version number.
- The reusable shape doesn't force consumers to fork or maintain their own copy of the templates.

**Non-Goals:**

- Generic build support. Consumers must be .NET. The reusable workflow runs `dotnet build` and `dotnet test` against the consumer's solution.
- CDK or Terraform alternatives. Plain CloudFormation, same as today.
- Open-source / public distribution. The NuGet package is restricted to this org's GitHub Packages feed.
- Configurable branch model. `app`/`beta`/`alpha`/`dev` shared-infrastructure branches are baked in. Configurable later if multiple consumers need different conventions.
- Per-consumer customization beyond what the documented input surface exposes. If a consumer wants a fundamentally different deployment shape (e.g., EKS instead of Fargate, RDS Postgres instead of Aurora MySQL), they should fork — that's not in scope.
- Auto-migration of existing repos onto the package. Each consumer makes that move themselves.

## Decisions

### D1. CloudFormation templates ship via a NuGet `contentFiles` package

The package `YadaYada.AwsWebApp.DeploymentStack` (working name) is a normal NuGet package whose `contentFiles` are the CloudFormation template YAML files. The csproj that produces it sets `<IncludeContentInPack>true</IncludeContentInPack>` and uses `<Content Include="infrastructure/**/*.template" Pack="true" PackagePath="contentFiles/any/any/infrastructure/" />` (or similar; exact paths TBD in implementation).

On the consumer side, a build target / workflow step extracts the templates from the resolved package to a known local path before the deploy job runs. Something like:

```yaml
- name: Extract DeploymentStack templates
  run: |
    PKG_DIR=$(dotnet nuget locals global-packages --list | awk '{print $NF}')/yadayada.awswebapp.deploymentstack/<version>
    cp -r $PKG_DIR/contentFiles/any/any/infrastructure/* ./infrastructure/
```

**Why NuGet:** This repo already publishes NuGet packages to GitHub Packages (`Tjb.Shared` / `Tjb.Data` / `Tjb.Api` / `Tjb.Migrations` / `Tjb.Web`). The publish infrastructure, auth, and consumer install patterns are well-trodden. Adding one more package fits the existing groove.

**Alternative considered:** S3-hosted templates. Reusable workflow uploads templates to a known bucket; consumers reference by URL. Rejected — versioning across S3 paths is fiddly, and the auth story (consumer accounts needing read access to the library's S3 bucket) is harder than NuGet's per-org-PAT-on-feed model.

**Alternative considered:** Templates inlined in the reusable workflow YAML via heredoc. Rejected — templates are large, embedded YAML-in-YAML is fragile, no syntax highlighting in editors.

**Alternative considered:** Templates committed to the consumer's `infrastructure/` directory directly, with periodic manual updates. Rejected — that's "fork and diverge," which is exactly what we're trying to avoid.

### D2. Reusable workflow lives in this repo, referenced by tag

The deploy workflow itself lives at `.github/workflows/deploy.yml` in this repo with `on: workflow_call:` at the top. Consumers call it as:

```yaml
# Consumer's .github/workflows/<their-workflow>.yml
jobs:
  deploy:
    uses: YadaYadaSoftware/awswebapp/.github/workflows/deploy.yml@v1
    with:
      project-name: my-app
      domain-name: example.com
      hosted-zone-id: Z1234567890ABC
      multi-region-branches: 'app,beta,alpha'
      secondary-region: us-west-2
      ... other inputs ...
    secrets:
      AWS_ACCESS_KEY_ID: ${{ secrets.AWS_ACCESS_KEY_ID }}
      AWS_SECRET_ACCESS_KEY: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
      AWS_ACCESS_KEY_ID_PROD: ${{ secrets.AWS_ACCESS_KEY_ID_PROD }}
      AWS_SECRET_ACCESS_KEY_PROD: ${{ secrets.AWS_SECRET_ACCESS_KEY_PROD }}
      DATABASE_PASSWORD: ${{ secrets.DATABASE_PASSWORD }}
      GOOGLE_CLIENT_ID: ${{ secrets.GOOGLE_CLIENT_ID }}
      GOOGLE_CLIENT_SECRET: ${{ secrets.GOOGLE_CLIENT_SECRET }}
```

**Versioning:** every merge to `app` runs the deploy stack's release-build job which (a) publishes a new NuGet version (SemVer) and (b) tags the commit as `v<major>.<minor>.<patch>`. Consumers pin to `@v1` (tracks the latest 1.x) or to a specific `@v1.4.2` for full reproducibility.

**Why a reusable workflow rather than a composite action:** composite actions run inline in the caller's job and can't define their own jobs (so no matrix, no per-region parallelism). The current deploy workflow uses a matrix across regions; that has to be in a workflow, not an action.

### D3. Configuration surface — workflow inputs vs. CFN parameters

Two kinds of project-specific values:

1. **Workflow-time variation** — things the CI workflow itself needs to know: project name, domain, hosted zone ID, container source path, .NET project paths. These become workflow `inputs`.
2. **Deploy-time variation** — things only the deployed CloudFormation stacks need: KMS key parameters, instance sizing, etc. These stay as CloudFormation template parameters.

A workflow input gets passed *into* the workflow, which then forwards it as a template parameter for deploy. For example: `project-name: my-app` (workflow input) becomes `--parameter-overrides ProjectName=my-app` (CFN deploy invocation).

**Minimum required inputs** (initial list; final TBD):
- `project-name` (required) — used in resource naming (`${project-name}-*` patterns)
- `domain-name` (required) — used for ACM cert + Route 53 records
- `hosted-zone-id` (required) — Route 53 hosted zone for DNS records
- `secondary-region` (default `us-west-2`) — replica region
- `multi-region-branches` (default `app,beta,alpha`) — comma-separated list of branches that deploy to both regions
- `prod-branch` (default `app`) — which branch triggers prod credentials
- `dotnet-version` (default `10.0.x`) — .NET SDK version
- `web-project-path` (default `src/<project-name>.Web`) — for Docker build context resolution
- `ui-test-filter` (default `FullyQualifiedName~UiTests`) — which tests to skip in the build job

**Secrets** (all required from consumer):
- `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` — nonprod CI credentials
- `AWS_ACCESS_KEY_ID_PROD` / `AWS_SECRET_ACCESS_KEY_PROD` — prod CI credentials
- `DATABASE_PASSWORD` — Aurora cluster admin password
- `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` — Google OAuth (if the consumer uses Google OAuth; should be optional eventually but required for now to match TaskManager's contract)
- `GITHUB_TOKEN` — auto-provided

### D4. CFN templates use `${ProjectName}` everywhere `taskmanager` is hardcoded

> **Baseline note:** this section predates the archived `domain-named-bootstrap-stack` / `domain-derived-resource-naming` work. The `taskmanager` literals it lists have already been replaced by `DomainName`/`${AWS::StackName}` derivations — e.g. KMS aliases are now `alias/${AWS::StackName}-aurora-{prod,nonprod}`, KMS SSM paths `/${AWS::StackName}/kms/*`, and Aurora cluster IDs / secrets paths derive from a locally-computed `DomainDashed`. So the remaining D4 work is introducing a `ProjectName` parameter where decoupling resource naming from the *domain* is desirable (the names below currently key off `DomainName`, not `taskmanager`), not removing `taskmanager`.

Every template would get a new `ProjectName` parameter at the top. The substitution sites below describe the *original* (pre-domain-derived) hardcoding for historical context — read `taskmanager-*` as "the current `DomainName`/`${AWS::StackName}`-derived name":

- `bootstrap.template` — KMS alias names (`alias/taskmanager-aurora-{prod,nonprod}` → `alias/${ProjectName}-aurora-{prod,nonprod}`), SSM parameter paths (`/taskmanager/kms/*` → `/${ProjectName}/kms/*`), `DeploymentPolicy` IAM policy resource scoping
- `db.template` — Aurora cluster identifiers, secrets manager paths (`taskmanager/database/regional/*` → `${ProjectName}/database/regional/*`)
- `security.template` (if it still exists at apply time — depends on whether `move-shared-lambda-role-to-bootstrap` has landed first) — IAM policy resource scoping
- `web.template` / `api.template` — ECS cluster naming, log group naming
- `dns.template` — Route 53 record names
- `network.template` — possibly nothing (VPC resources don't usually carry project-specific names)

The workflow passes `ProjectName=${project-name}` (the input) to every CFN deploy as a parameter override.

### D5. TaskManager re-onboards itself as the first consumer

The same repo will be both the library AND its first consumer. Concretely:

- `infrastructure/` directory stays in this repo as the source of truth. Edits go here, and on merge to `app`, a release-build job packs the templates into the NuGet package and publishes.
- `.github/workflows/deploy.yml` is the reusable workflow (the new file).
- `.github/workflows/zbuild.yml` is renamed (or restructured) into a thin caller that runs TaskManager's .NET build steps and then `uses: ./.github/workflows/deploy.yml` (self-reference; works because reusable workflows can be called from the same repo at a path-relative ref).

**Why this matters:** every change to the library is tested against TaskManager itself before being tagged for downstream consumers. We get "eat your own dogfood" without any extra effort. If a refactor of the reusable workflow breaks TaskManager, we know on the merge that produced it.

### D6. Publication cadence and version semantics

- Every merge to `app` publishes a new NuGet version. Version comes from `GitVersion` (already in use here), in the form `<major>.<minor>.<patch>[-<branch-suffix>]`. For `app` merges, no suffix → public.
- A matching git tag `v<major>.<minor>.<patch>` is created in the same workflow run.
- Consumers can `@v1` (auto-tracks latest 1.x), `@v1.4` (latest 1.4.x), `@v1.4.2` (exact), or `@main` (HEAD, unstable).
- Breaking changes (input renames, removed inputs, CFN template parameter renames) bump the major version.

## Risks / Trade-offs

- **[Risk] Consumer projects pull templates from a NuGet package; if the NuGet feed goes down (GitHub Packages outage), deploys break.** → Same risk as today for the existing Tjb.* packages; same risk profile is acceptable. Mitigate: consumers can pin to a specific version and `dotnet restore` typically caches packages on the GitHub Actions runner.
- **[Risk] The reusable workflow's input contract becomes a hard-to-change interface once multiple consumers depend on it.** → Mitigate: SemVer discipline; deprecate inputs with two-minor-version overlap before removing; document deprecation in CHANGELOG.md.
- **[Risk] TaskManager-specific edge cases get baked into the "reusable" templates because they were never separated.** → Mitigate: during the extraction, every hardcoded literal gets reviewed against the "is this taskmanager-specific or universally true?" question. The acceptance test is "could a *different* .NET web app use this without modification?" — answered by the second consumer's onboarding when/if that happens.
- **[Risk] The bootstrap stack is shared across consumer projects in the same AWS account, but the current bootstrap design assumes one project's resource naming.** → Mitigate: this is actually fine; bootstrap's KMS keys (`alias/${ProjectName}-aurora-*`), IAM users (`${ProjectName}GitHubActionsUser`), SSM paths (`/${ProjectName}/*`) all get project-name-scoped. Two consumer projects in the same AWS account get their own bootstrap stacks with non-overlapping resources. The truly account-singleton resources (`AWS::ApiGateway::Account`, the `cf-templates-{account}-{region}` S3 bucket policy) are a per-account concern that needs careful handling — see Open Questions.
- **[Risk] TaskManager's first consumer-of-itself rollout could break TaskManager's own CI.** → Mitigate: tasks.md sequences this so TaskManager keeps using its current `zbuild.yml` (renamed) until the new reusable workflow is fully landed and tested in parallel; switchover happens deliberately.
- **[Trade-off] Versioning and release cadence become real concerns the project didn't have before.** → Acceptable. The discipline is worth the reuse leverage.
- **[Trade-off] An extra workflow step (extracting templates from the NuGet package) on every deploy adds ~10-15 seconds.** → Acceptable.
- **[Trade-off] Consumers using `@v1` get auto-upgrades to new minor versions, which may cause friction if the library changes a default behavior.** → Mitigate: SemVer discipline; behavioral changes that aren't strictly additive get major-version bumps.

## Migration Plan

**Phase 1 — Template parameterization (internal to this repo, no consumer changes yet)**
1. Audit every template in `infrastructure/` for hardcoded `taskmanager`/`appcloud.systems`/`Tjb.Web`/other project-specific strings.
2. Introduce `ProjectName`, `DomainName`, and any other identified parameters into each template.
3. Update `bootstrap.template` first (it owns the most parameter-able names).
4. Update the rest of the chain (`master`, `backend`, `db`, etc.) to thread the new parameters through.
5. Update `zbuild.yml` to pass the new parameters with TaskManager's current values (`ProjectName=taskmanager`, etc.). Deploy to verify nothing visible changes.

**Phase 2 — Package the templates**
6. Create a new .NET project (e.g., `src/YadaYada.AwsWebApp.DeploymentStack/`) that packs the `infrastructure/` directory as NuGet content.
7. Add the new project to the existing NuGet publish job in `zbuild.yml` (or its successor).
8. Verify the package publishes correctly to GitHub Packages and a fresh download contains all expected templates.

**Phase 3 — Extract reusable workflow**
9. Create `.github/workflows/deploy.yml` as a `workflow_call`-shaped workflow. Move the bulk of `zbuild.yml`'s `deploy` job into it.
10. Define the input/secret surface from D3.
11. Update `zbuild.yml` to be the TaskManager-specific caller: runs `.NET` build + test + NuGet publish jobs, then calls `./.github/workflows/deploy.yml` with TaskManager's inputs.
12. Verify TaskManager still deploys end-to-end via the new workflow shape on a feature branch.

**Phase 4 — Documentation and release**
13. Write `CONSUMING.md` at repo root: prereqs, onboarding steps, input reference, secret reference, troubleshooting.
14. Tag the merge as `v1.0.0`.
15. Announce internally (org slack / repo README).

**Rollback strategies:**
- Phase 1: each commit is reversible by git revert; deploy verifies on feature branch first.
- Phase 2: NuGet package is additive; reverting the publish doesn't break TaskManager.
- Phase 3: if the new reusable workflow breaks, revert `zbuild.yml` to the pre-extract state. The reusable workflow file can stay (unused) until fixed.
- Phase 4: a bad `v1.0.0` can be superseded by `v1.0.1`; consumers pinned to `@v1` get the fix automatically.

## Open Questions

1. **AWS account model — one account per consumer, or shared?** Currently TaskManager has its own account. If a second consumer lands in the *same* account, the per-account singletons (`AWS::ApiGateway::Account`, the templates-bucket policy) need to be either shared (one consumer "wins" ownership) or moved out to a higher-level account-bootstrap stack. Likely answer: each consumer gets its own AWS account; document that assumption clearly. Confirm before tagging v1.0.0.
2. **NuGet package name.** `YadaYada.AwsWebApp.DeploymentStack` is a working name. Could also be `YadaYada.DeploymentStack`, `YadaYada.WebApp.AwsDeploy`, etc. Pick during implementation; once tagged it's hard to change.
3. **Does the reusable workflow assume Aurora MySQL specifically, or accept Postgres as a future variant?** Initial implementation is Aurora MySQL (matching TaskManager). Adding Postgres variant is straightforward (db.template branches on engine) but adds input-surface complexity. Defer.
4. **Versioning across templates and workflow — single SemVer track or independent?** Single track simpler; pick that unless we hit a reason to decouple.
5. **Consumer's container build — assume the consumer provides their `Dockerfile` at a known path, or accept the Dockerfile path as an input?** Latter is more flexible. Use a `web-dockerfile-path` input with a sensible default.
