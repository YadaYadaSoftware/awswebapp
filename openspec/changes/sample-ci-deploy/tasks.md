## 1. Prereqs and dependency reconciliation

- [ ] 1.1 Confirm `make-deployment-stack-reusable` is landed and the reusable `deploy.yml` + `YadaYada.AwsWebApp.DeploymentStack` package are consumable. **Do not start section 4 until this is true.**
- [ ] 1.2 Confirm `sample-solution-local` is merged: `Sample.sln` builds from the feed, `Sample.Web` has a Dockerfile and migrations runner.
- [ ] 1.3 Read the *actual* finalized reusable-workflow input/secret surface and reconcile it against this change's assumed inputs; note any renames.
- [ ] 1.4 Resolve design Open Questions with the user: sample domain/subdomain + hosted zone (Q1), AWS account model (Q2), whether to deploy to shared-infra branches or feature-only (Q3).
- [ ] 1.5 Branch from `dev` with the spec's exact name: `git checkout dev; git pull; git checkout -b sample-ci-deploy`.

## 2. AWS prerequisites (manual, documented)

- [ ] 2.1 Per the chosen account model, ensure the sample's bootstrap stack exists (own account or `ProjectName`-scoped in the shared account) with KMS/IAM/ECR/SSM provisioned by the reusable bootstrap.
- [ ] 2.2 Ensure Route 53 hosted zone + ACM/DNS for the sample's domain/subdomain.
- [ ] 2.3 If the sample sends email, run the per-region SES identity setup for its domain (or reuse a verified identity); otherwise document that the post-registration email path is disabled for the sample.
- [ ] 2.4 Capture these as a `src/sample/DEPLOYING.md` (or section in the sample README) so the manual prerequisites are reproducible.

## 3. Sample CI build/test

- [ ] 3.1 Add the sample build/test steps (`dotnet restore`/`build`/`test` for `Sample.sln`, restoring the framework + `Tjb.Shared` from GitHub Packages with the CI PAT).
- [ ] 3.2 Confirm the sample's migrations apply on container startup (the `ApplyDatabaseMigrationsAsync<SampleDbContext>` path), matching `Tjb.Web`.

## 4. Deploy via the reusable workflow

- [ ] 4.1 Add the sample's CI workflow (path/branch-scoped, or a dedicated job) that runs section 3, then `uses:` the reusable `deploy.yml` with the sample's inputs (`project-name`, `domain-name`, `hosted-zone-id`, `web-project-path=src/sample/Sample.Web`, Dockerfile path, `secondary-region`, `multi-region-branches`, `prod-branch`, `ui-test-filter`) and secrets.
- [ ] 4.2 Use a distinct `ProjectName` (e.g. `sample`) and distinct branch leaves so the sample's resource names and stack names never collide with TaskManager's.
- [ ] 4.3 **Checkpoint (feature branch, single region, cheap):** push a sample feature branch; confirm it builds, deploys a single-region stack, and resolves at `https://<sample-leaf>.{DOMAIN_NAME}`.

## 5. Deployed-URL UI tests

- [ ] 5.1 Create/finish `src/sample/Sample.UiTests` (Playwright/xUnit) targeting `https://<sample-leaf>.{DOMAIN_NAME}`; exercise login + the Guestbook page; use the token-based Google auth approach as `Tjb.UiTests`.
- [ ] 5.2 Wire UI tests into the deploy flow with a pinned TRX (`sample-ui-tests.trx`) → `dorny/test-reporter` summary + `trace.zip` artifact upload, gating the deploy as TaskManager's pipeline does.
- [ ] 5.3 **Checkpoint:** UI tests pass against the deployed sample feature-branch URL.

## 6. Tagging + cleanup parity

- [ ] 6.1 Verify the sample's deployed stacks carry the six stack-level tags (`Stack Name`, `Create Date`, `Branch`, `Specification`, `Version`, `DeployRunUrl`).
- [ ] 6.2 **Checkpoint:** delete a sample feature branch; confirm the cleanup workflow tears down only that sample stack and clears its templates-bucket prefix, leaving TaskManager + bootstrap untouched. (Mind the default-branch dormancy caveat — cleanup runs from `app`.)

## 7. Shared-infrastructure deploys (gated)

- [ ] 7.1 Only if the user approved (Q3): deploy the sample to `dev`, then `alpha`/`beta`/`app`, confirming multi-region stacks and `https://<sample-leaf>.{DOMAIN_NAME}` in both regions on the shared-infra branches.
- [ ] 7.2 **Checkpoint (end goal):** two multi-regional web applications — TaskManager and the sample — run from the same framework, each with its own stacks/URL.

## 8. Docs, validation, archive

- [ ] 8.1 Document the sample's deploy story in `src/sample/DEPLOYING.md`; report any reusable-stack gaps back to `make-deployment-stack-reusable`.
- [ ] 8.2 `openspec validate sample-ci-deploy --strict` and resolve issues.
- [ ] 8.3 Verify each scenario in `specs/sample-ci-deployment/spec.md`.
- [ ] 8.4 Archive this change (`/opsx:archive`) once merged and validated.
