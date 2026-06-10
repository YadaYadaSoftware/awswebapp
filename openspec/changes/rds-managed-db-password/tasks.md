## 1. Verify feasibility (resolve blocking unknowns before any prod-path change)

- [ ] 1.1 Confirm `ManageMasterUserPassword: true` is valid on the primary Aurora **Global Cluster** member and that the secondary member keeps `AWS::NoValue` (no second managed secret).
- [ ] 1.2 Resolve the secondary-region secret-delivery question (design Open Question #1): decide between a Secrets Manager **replica secret** in the secondary region vs. a cross-region read of the primary secret; prove it on a multi-region test stack.
- [ ] 1.3 Confirm the RDS rotation window keeps the previous password valid long enough that re-fetch-on-auth-failure always succeeds; decide on an initial rotation interval.

## 2. Runtime secret resolver in the framework (D2 + D4)

- [ ] 2.1 Add a connection-string/password provider in `Tjb.Web.Hosting` that reads the managed secret (JSON `password`) at startup from a `DB_SECRET_ARN`-style input and **re-reads on a DB auth failure**; build the connection string from existing host/db/username inputs.
- [ ] 2.2 Wire `Tjb.Web` (and the sample host) to use the provider instead of a pre-resolved `ConnectionStrings__DefaultConnection`.
- [ ] 2.3 Grant the ECS task role `secretsmanager:GetSecretValue` + `kms:Decrypt` scoped to the managed secret / Aurora KMS key in `infrastructure/security.template` (mirror the existing `SesAccess` policy).
- [ ] 2.4 Verify locally/dev that the app reads the password at runtime against the *existing* self-managed secret (no behavior change yet).

## 3. Managed secret + new export in db.template (D1 + D3)

- [ ] 3.1 `infrastructure/db.template`: add `ManageMasterUserPassword: true` (+ `MasterUserSecret.KmsKeyId: !Ref AuroraKmsKeyArn` under `IsPrimary`), remove `MasterUserPassword`, keep `MasterUsername`.
- [ ] 3.2 Add a new export `DatabaseMasterSecretArn-${BranchName}-${DomainDashed}` = `!GetAtt AuroraCluster.MasterUserSecret.SecretArn` (canonical `GetAtt` value; controlled `Export.Name` per `cross-stack-export-naming`). Leave the old `DatabasePasswordSecret` + export in place for now.

## 4. Consumers read the managed secret (web/api/feature)

- [ ] 4.1 `infrastructure/web.template` + `infrastructure/api.template`: stop resolving the password into the connection string at deploy time; pass the managed secret ARN (import `DatabaseMasterSecretArn`) to the container for the runtime provider.
- [ ] 4.2 Confirm feature-branch (application.template) stacks import the new export and authenticate.

## 5. Remove DATABASE_PASSWORD from the pipeline + templates

- [ ] 5.1 `.github/workflows/deploy.yml`: remove the `DatabasePassword=` parameter line and drop `DATABASE_PASSWORD` from the `workflow_call` required secrets.
- [ ] 5.2 Remove the `DatabasePassword` parameter from `master.template`, `backend.template`, `db.template` (and the threading between them).
- [ ] 5.3 Remove the self-managed `DatabasePasswordSecret` resource and the old `DatabasePasswordSecretArn` export once no stack imports it.

## 6. Preflight + docs/spec cleanup

- [ ] 6.1 `.github/workflows/sample-deploy.yml`: drop the `DATABASE_PASSWORD` check from the `validate-config` preflight (and its `env:` wiring).
- [ ] 6.2 Remove `DATABASE_PASSWORD` from the required-secrets checklists in `README.md`, `src/sample/README.md`, `src/sample/DEPLOYING.md`, `CONSUMING.md`, and `src/docs/*`.
- [ ] 6.3 Reconcile the in-flight `standalone-fork-readiness` change (its preflight requirement + README/DEPLOYING checklists list `DATABASE_PASSWORD`) so the two changes don't conflict at archive.
- [ ] 6.4 Confirm the `reusable-deployment-stack` spec delta (required secrets minus `DATABASE_PASSWORD`) matches the implemented workflow.

## 7. Rollout (dev → test → app) and verification

- [ ] 7.1 Roll out phases 2→5 on `dev`; verify the app authenticates with the managed password.
- [ ] 7.2 Promote to `test` (multi-region) and **force a rotation**; verify both regions re-authenticate without redeploy (proves §1.2 + §2.1).
- [ ] 7.3 Snapshot + rehearse rollback on `test`, then promote to `app`; verify production authenticates post-rotation.

## 8. Validate + archive

- [ ] 8.1 `openspec validate rds-managed-db-password --strict`.
- [ ] 8.2 Archive once merged through `dev`→`test`→`app` and verified in production.
