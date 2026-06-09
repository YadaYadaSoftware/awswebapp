## 1. Prereqs and dependency reconciliation

- [x] 1.1 Confirm `make-deployment-stack-reusable` landed + consumable. — *Done: `.github/workflows/deploy.yml` (workflow_call) and `YadaYada.AwsWebApp.DeploymentStack` are on `dev` (this branch's base `281c997`).*
- [x] 1.2 Confirm `sample-solution-local` merged. — *Done: `src/sample/Sample.sln` builds from the feed; `Sample.Web` has a Dockerfile + `Sample.Migrations` runner (verified during that change).*
- [x] 1.3 Reconcile against the actual reusable input/secret surface. — *DONE — and it is NOT thin (gap to report upstream). The real `deploy.yml` inputs: `branch-name`, `environment`, `custom-version`, `assembly-sem-ver`, `domain-name`, `hosted-zone-id`, `region-primary`, `region-secondary`, `multi-region-branches`, `shared-infra-branches`, `prod-branch`, `engine-version`, `dotnet-version`, `web-dockerfile-path`, `web-image-name`, `ui-tests-project-path`, `deployment-stack-version`, `deployment-stack-feed`. **Findings:** (1) **No `project-name` / no `ProjectName` axis** — §0.1 of make-deployment-stack-reusable decided derive-from-domain, so D2's "distinct ProjectName, same domain" coexistence is impossible; the only isolation axis is the **domain**. Sharing `appcloud.systems` means sharing TaskManager's bootstrap (KMS/ECR/SSM) and, on feature branches, importing **dev's backend Aurora** → the sample's Identity/`AspNetUsers` migration would collide in TaskManager's dev DB. → the sample needs its **own domain** for clean coexistence. (2) `web-project-path`→ use `web-dockerfile-path`(`src/sample/Sample.Web/Dockerfile`)+`web-image-name`(`sample-web`). (3) `ui-test-filter`→ `ui-tests-project-path`(`src/sample/Sample.UiTests`). (4) `secondary-region`→ `region-primary`+`region-secondary`. (5) In-repo caller ⇒ checkout already has `infrastructure/`, so `deployment-stack-version` stays empty (no extraction). Design D1/D2 + Open Questions updated.*
- [x] 1.4 Resolve design Open Questions. — *DECIDED: **Q1** `domain-name = sample.appcloud.systems` (own dashed namespace `sample-appcloud-systems`; DNS in the existing `appcloud.systems` zone). **Q2** same AWS account, isolated by the dashed domain (own bootstrap `sample-appcloud-systems`); the `AWS::ApiGateway::Account` / templates-bucket singletons must be handled per make-deployment-stack-reusable 1.3 (documented in DEPLOYING.md). **Q3** feature-branch (single-region) first; shared-infra (`app`/`beta`/`alpha`, multi-region, 2nd Aurora) gated behind explicit approval (§7).*
- [x] 1.5 Branch from `dev` with the spec's exact name. — *Done: `sample-ci-deploy` off dev tip `281c997` (wilhelm worktree).*

## 2. AWS prerequisites (manual, documented)

- [ ] 2.1 **(operator)** Deploy the sample bootstrap stack `sample-appcloud-systems` per region. — *Documented in DEPLOYING.md (incl. the ApiGateway/templates-bucket singleton caveat). Operator AWS action; not done in scaffolding.*
- [ ] 2.2 **(operator)** Route 53 / ACM for `*.sample.appcloud.systems` (records in the existing zone `Z06422172SASV44F5Y8VA`). — *Documented; operator AWS action.*
- [ ] 2.3 **(operator)** Per-region SES for `sample.appcloud.systems` (or accept no confirmation email). — *Documented; operator AWS action.*
- [x] 2.4 Capture the prerequisites in `src/sample/DEPLOYING.md`. — *Done: full operator runbook — naming model (subdomain-as-domain), per-region bootstrap, DNS/ACM, sample `dev` Aurora, SES, the `SAMPLE_DEPLOY_ENABLED` enable switch, cost gate, and the reconciled input mapping.*

## 3. Sample CI build/test

- [x] 3.1 Sample build/test steps. — *Done in `sample-deploy.yml`'s `build-sample` job: `dotnet restore`/`build` `Sample.sln` with `GH_PACKAGES_TOKEN=secrets.GITHUB_TOKEN` + `packages: read`. (No unit-test project in the sample; UI tests run post-deploy.) `dotnet build` of the sample was verified locally during scaffolding.*
- [x] 3.2 Migrations apply on container startup. — *Confirmed: `Sample.Web/Program.cs` calls `app.ApplyDatabaseMigrationsAsync<SampleDbContext>()` (the framework's migrate-on-startup helper), matching `Tjb.Web`.*

## 4. Deploy via the reusable workflow

- [x] 4.1 Sample CI workflow calling the reusable `deploy.yml`. — *Done: `.github/workflows/sample-deploy.yml` (path-scoped to `src/sample/**`) → `uses: ./.github/workflows/deploy.yml` with the reconciled inputs (`domain-name=sample.appcloud.systems`, `hosted-zone-id`, `region-primary/secondary`, `web-dockerfile-path=src/sample/Sample.Web/Dockerfile`, `web-image-name=sample-web`, `ui-tests-project-path=src/sample/Sample.UiTests`), `secrets: inherit`. **Gated `if: vars.SAMPLE_DEPLOY_ENABLED == 'true'`** so it's a no-op until the operator provisions AWS. YAML validated.*
- [x] 4.2 Distinct namespace via the domain (no ProjectName axis). — *Done: `sample.appcloud.systems` → dashed `sample-appcloud-systems`, distinct bootstrap/KMS/ECR/SSM/Aurora + stack names `{leaf}-sample-appcloud-systems`. (Updated from D2's ProjectName approach per the 1.3 reconciliation.)*
- [ ] 4.3 **Checkpoint (operator):** enable + push a sample feature branch; confirm single-region deploy resolves at `https://<leaf>.sample.appcloud.systems`. — *GATED on the AWS prereqs + `SAMPLE_DEPLOY_ENABLED=true`.*

## 5. Deployed-URL UI tests

- [x] 5.1 Create `src/sample/Sample.UiTests` (Playwright/xUnit). — *Done (minimal smoke): `SmokeTests` hits `TEST_BASE_URL` (defaults to `https://dev.sample.appcloud.systems`), asserts a successful response + the login surface renders. Builds clean. **Full token-based Google auth + authenticated-Guestbook coverage (mirroring `Tjb.UiTests`) is deferred** — it can only run against a live deployed URL; noted in the file.*
- [x] 5.2 Wire UI tests into the deploy flow. — *Done via the `ui-tests-project-path=src/sample/Sample.UiTests` input to `deploy.yml` — its post-deploy UI-test job runs this project against `https://<leaf>.sample.appcloud.systems` with the same `dorny/test-reporter` summary + artifact upload as TaskManager. (Note: `deploy.yml` fixes the TRX name as `ui-tests.trx`, not `sample-ui-tests.trx` — minor divergence from the design, harmless since runs are separate.)*
- [ ] 5.3 **Checkpoint (operator):** UI tests pass against the deployed sample URL. — *GATED on the deploy (4.3).*

## 6. Tagging + cleanup parity

- [ ] 6.1 Verify the sample's deployed stacks carry the six stack-level tags (`Stack Name`, `Create Date`, `Branch`, `Specification`, `Version`, `DeployRunUrl`).
- [ ] 6.2 **Checkpoint:** delete a sample feature branch; confirm the cleanup workflow tears down only that sample stack and clears its templates-bucket prefix, leaving TaskManager + bootstrap untouched. (Mind the default-branch dormancy caveat — cleanup runs from `app`.)

## 7. Shared-infrastructure deploys (gated)

- [ ] 7.1 Only if the user approved (Q3): deploy the sample to `dev`, then `alpha`/`beta`/`app`, confirming multi-region stacks and `https://<sample-leaf>.{DOMAIN_NAME}` in both regions on the shared-infra branches.
- [ ] 7.2 **Checkpoint (end goal):** two multi-regional web applications — TaskManager and the sample — run from the same framework, each with its own stacks/URL.

## 8. Docs, validation, archive

- [x] 8.1 Document the deploy story + report reusable-stack gaps. — *Done: `src/sample/DEPLOYING.md`. **Gap reported (here, since make-deployment-stack-reusable is already archived):** the reusable stack's derive-from-domain decision means consumers have **no `ProjectName` isolation axis** — to coexist in one account a consumer must use its **own (sub)domain** as `domain-name`, and the per-account singletons (`AWS::ApiGateway::Account`, templates-bucket policy) need one owner. Captured in design.md (D2 reconciliation) + DEPLOYING.md for any future consumer.*
- [x] 8.2 `openspec validate sample-ci-deploy --strict`. — *passed.*
- [ ] 8.3 **(operator)** Verify each scenario in `specs/sample-ci-deployment/spec.md` against the deployed sample. — *Gated on the deploy; scenarios about build/inputs are satisfied by scaffolding, the deployed-URL/cleanup/multi-region scenarios need 4.3/§6/§7.*
- [ ] 8.4 Archive this change once merged and validated. — *Pending the operator deploy + merge to dev.*
