## 1. Prereqs and audit

- [ ] 1.1 Confirm the in-flight foundational changes are done or accounted for: `centralize-aurora-kms-keys` deployed to all four env stacks; `robust-aurora-cluster-teardown` and `move-shared-lambda-role-to-bootstrap` are either landed or explicitly deferred until *after* this change. Working order: KMS centralization first (already underway), then this change, then the others can land in any order.
- [ ] 1.2 Decide and document the final NuGet package name. Working name in this proposal is `YadaYada.AwsWebApp.DeploymentStack`. Once tagged it's hard to change; lock in before Phase 2.
- [ ] 1.3 Decide and document the AWS-account model assumption: one consumer project per AWS account, OR multiple consumers per account with namespace isolation. The proposal recommends one-per-account; confirm before tagging v1.0.0.
- [ ] 1.4 Audit hardcoded project-specific values: `grep -rn 'taskmanager' infrastructure/`, `grep -rn 'appcloud.systems' infrastructure/ .github/`, `grep -rn 'Tjb' infrastructure/ .github/`. Save the result as a checklist for Phase 1.

## 2. Phase 1 — Template parameterization (internal, no consumer changes)

- [ ] 2.1 Add `ProjectName` parameter (`Type: String`, no default — required) to [infrastructure/bootstrap.template](../../../infrastructure/bootstrap.template). Replace every `taskmanager` literal with `!Sub "${ProjectName}-..."` (or equivalent for KMS alias names, SSM paths, IAM policy resource patterns). The SSM key paths become `/${ProjectName}/kms/{prod,nonprod}/aurora-key-arn`.
- [ ] 2.2 Add `ProjectName` parameter to [infrastructure/master.template](../../../infrastructure/master.template), thread to all nested stacks. Add `DomainName` parameter if not already there (currently it's a parameter but with `Default: "appcloud.systems"` — remove the default so the consumer must specify).
- [ ] 2.3 Add `ProjectName` parameter to [infrastructure/backend.template](../../../infrastructure/backend.template). Replace any project-name literals.
- [ ] 2.4 [infrastructure/db.template](../../../infrastructure/db.template): `taskmanager-${BranchName}-global-cluster` becomes `${ProjectName}-${BranchName}-global-cluster`; the secrets-manager path `taskmanager/database/regional/*` becomes `${ProjectName}/database/regional/*`. Aurora cluster identifiers use `${ProjectName}` prefix.
- [ ] 2.5 [infrastructure/security.template](../../../infrastructure/security.template) (if still present at apply time): replace `taskmanager-*` IAM policy resource patterns with `${ProjectName}-*`. (If `move-shared-lambda-role-to-bootstrap` has landed, this file may not exist; skip.)
- [ ] 2.6 [infrastructure/web.template](../../../infrastructure/web.template), [infrastructure/api.template](../../../infrastructure/api.template), [infrastructure/dns.template](../../../infrastructure/dns.template), [infrastructure/network.template](../../../infrastructure/network.template), [infrastructure/infrastructure.template](../../../infrastructure/infrastructure.template): audit and parameterize. ECS cluster name, log group name, target group names, etc.
- [ ] 2.7 Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s "Set parameter overrides" step to pass `ProjectName=taskmanager DomainName=appcloud.systems` for now (so this repo's deploys keep working with TaskManager values).
- [ ] 2.8 `aws cloudformation validate-template` on all touched templates.
- [ ] 2.9 Push to a throwaway feature branch and verify the deploy still works (templates render correctly with the parameterization, no resource-naming surprises).
- [ ] 2.10 Push to dev to confirm the master-template branches still work end-to-end. The dev env stack may need a CFN UPDATE to pick up the new `ProjectName` parameter. Verify resource names didn't change (they shouldn't, since we're passing the same literal `taskmanager` value).
- [ ] 2.11 Grep audit: `grep -r "taskmanager" infrastructure/` returns zero hits (other than comments). `grep -r "appcloud.systems" infrastructure/` returns zero hits.

## 3. Phase 2 — NuGet package the templates

- [ ] 3.1 Create new project `src/YadaYada.AwsWebApp.DeploymentStack/YadaYada.AwsWebApp.DeploymentStack.csproj`. Minimal SDK-style csproj with `<IsPackable>true</IsPackable>`, package metadata (id, authors, repository URL, etc.), and `<Content>` items that include every file in the `infrastructure/` directory with `Pack="true" PackagePath="contentFiles/any/any/infrastructure/"`.
- [ ] 3.2 Add the new project to [Tjb.sln](../../../Tjb.sln) (or whatever solution file is current).
- [ ] 3.3 Build locally: `dotnet pack src/YadaYada.AwsWebApp.DeploymentStack -o ./nupkgs`. Inspect the produced `.nupkg` (it's a zip) — confirm `contentFiles/any/any/infrastructure/*.template` is present.
- [ ] 3.4 Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)'s `Pack` steps section (in the build job, where `Pack Tjb.Shared` etc. live) to add a `Pack YadaYada.AwsWebApp.DeploymentStack` step following the same SemVer + branch-suffix conventions.
- [ ] 3.5 The existing `publish-nuget` job picks up all packages in `./nupkgs/`, so no further publish-job changes needed — verify on the next CI run.
- [ ] 3.6 Tag the merge on the working branch as a pre-release (`v1.0.0-preview.1`) and confirm the package shows up on GitHub Packages.

## 4. Phase 3 — Extract the reusable workflow

- [ ] 4.1 Create new workflow file `.github/workflows/deploy.yml` with `on: workflow_call:` at the top. Define inputs per design.md D3 (project-name, domain-name, hosted-zone-id, secondary-region, multi-region-branches, prod-branch, dotnet-version, web-project-path, web-dockerfile-path) and secrets (AWS_ACCESS_KEY_ID / SECRET / *_PROD, DATABASE_PASSWORD, GOOGLE_CLIENT_ID / SECRET).
- [ ] 4.2 Move the bulk of `zbuild.yml`'s `deploy` job into `deploy.yml`. Replace references to `secrets.X` and hardcoded values with `inputs.X` and `secrets.X` (passed in from caller). Replace `${{ secrets.DOMAIN_NAME }}` → `${{ inputs.domain-name }}`, etc. Keep the existing select-creds / SSM-lookup / parameter-overrides logic — those need to be parameterized to use `${{ inputs.project-name }}` in SSM paths and stack names.
- [ ] 4.3 Add a new "Extract DeploymentStack templates" step at the very top of the deploy job in `deploy.yml`. The step (a) determines the package path in the dotnet global-packages cache, (b) copies `contentFiles/any/any/infrastructure/*` into `./infrastructure/` of the runner. The reusable workflow caller (`zbuild.yml`) must have installed the package via the build job's `dotnet restore` before calling `deploy.yml`.
- [ ] 4.4 Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) to be the TaskManager-specific caller:
  - Keep the .NET build, test, pack-nuget steps unchanged (they're TaskManager-specific build logic that runs before deploy).
  - Add a step that ensures the new `YadaYada.AwsWebApp.DeploymentStack` package is installed (probably by including it as a PackageReference in `Tjb.Web.csproj` or a dedicated `infrastructure/Infrastructure.csproj`, so `dotnet restore` resolves it).
  - Add a `deploy` job that does `uses: ./.github/workflows/deploy.yml` and provides TaskManager's specific inputs and secrets.
- [ ] 4.5 Verify on a throwaway feature branch that the new shape works end-to-end (`zbuild.yml` builds + tests, then calls `deploy.yml`, which extracts templates + deploys).
- [ ] 4.6 Verify on dev that master-template deploys still work via the new shape.

## 5. Phase 4 — Documentation and release

- [ ] 5.1 Write `CONSUMING.md` at repo root. Sections: prereqs, onboarding steps (1-9 numbered), input reference (table), secret reference (table), branch model, troubleshooting. Include a complete example consumer workflow YAML.
- [ ] 5.2 Update [README.md](../../../README.md) and [CLAUDE.md](../../../CLAUDE.md) to reflect that this repo is also a library; point to `CONSUMING.md`. Don't delete the existing project-specific docs (TaskManager is still a real consumer).
- [ ] 5.3 Tag the merge on `app` as `v1.0.0` (or whatever GitVersion produces). The NuGet package version matches the tag.
- [ ] 5.4 Announce internally (org Slack / awareness email) that `YadaYadaSoftware/awswebapp` is now consumable as a deployment stack; point to `CONSUMING.md`.

## 6. Validation

- [ ] 6.1 Run `openspec validate make-deployment-stack-reusable --strict` and resolve any issues.
- [ ] 6.2 Manually verify each `## Requirement` scenario from `specs/reusable-deployment-stack/spec.md`:
  - NuGet package publishes on `app` merge; pre-release version on other branches.
  - Templates extractable from `contentFiles/any/any/infrastructure/`.
  - Reusable workflow callable from another repo (test with a dummy repo if no real second consumer is ready yet).
  - TaskManager self-consumes via `uses: ./.github/workflows/deploy.yml`.
  - Required inputs and secrets enforced (try omitting one; verify the workflow fails clearly).
  - `grep` audit confirms no `taskmanager` or `appcloud.systems` literals in `infrastructure/`.
  - `CONSUMING.md` documents every input the workflow takes (no drift).
- [ ] 6.3 Archive this change per the experimental workflow (`/opsx:archive`).
