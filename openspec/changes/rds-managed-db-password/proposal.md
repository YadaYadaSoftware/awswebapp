## Why

The Aurora master password is supplied as an externally-managed value: the `DATABASE_PASSWORD`
GitHub secret is passed as a CloudFormation parameter on master-template deploys, used to set the
cluster's `MasterUserPassword`, **and** copied verbatim into a self-managed Secrets Manager secret the
app reads. That means a human picks/rotates the credential, it lives in three places (GitHub, the
cluster, the secret), and there is no rotation. AWS RDS can own this entirely: `ManageMasterUserPassword`
makes RDS generate, store, and rotate the password in a managed Secrets Manager secret — removing the
GitHub secret and the hand-managed secret resource. This also shrinks the fork onboarding surface (one
fewer required secret in the config preflight).

## What Changes

- **BREAKING (operator config):** the `DATABASE_PASSWORD` GitHub secret and the `DatabasePassword`
  CloudFormation parameter are **removed**. RDS generates and owns the master password.
- `infrastructure/db.template`: the primary `AuroraCluster` sets `ManageMasterUserPassword: true`
  (master secret encrypted with the existing Aurora KMS key), drops `MasterUserPassword`, and keeps
  `MasterUsername`. The self-managed `DatabasePasswordSecret` resource is replaced by the cluster's
  `MasterUserSecret`; the `DatabasePasswordSecretArn` export now points at the managed secret ARN.
- **Connection-string sourcing changes** because the managed secret is JSON (`{username, password}`),
  not a raw string, **and auto-rotates (~7 days)** — so the current deploy-time `{{resolve:…:SecretString}}`
  injection into a static ECS env var is no longer viable (it would go stale after the first rotation).
  The app must read the password from the managed secret **at runtime**. (See design.md — this is the
  central decision and the largest piece of work.)
- `.github/workflows/deploy.yml`: remove the `DatabasePassword=` parameter line and drop
  `DATABASE_PASSWORD` from the `workflow_call` required secrets. `master.template` / `backend.template`
  / `db.template` drop the `DatabasePassword` parameter threading.
- **Docs/spec cleanup (downstream):** remove `DATABASE_PASSWORD` from the `sample-deploy.yml`
  `validate-config` preflight and from every checklist that mirrors it — root `README.md`,
  `src/sample/README.md`, `src/sample/DEPLOYING.md`, `CONSUMING.md`, `src/docs/*`, and the
  `reusable-deployment-stack` spec's required-secrets set. The in-flight `standalone-fork-readiness`
  change's preflight/checklist must be reconciled too (it currently lists `DATABASE_PASSWORD` required).

## Capabilities

### New Capabilities

- `rds-managed-db-password`: the contract that the Aurora master credential is RDS-managed — no
  externally-supplied password parameter or GitHub secret; the password lives only in the RDS-managed
  Secrets Manager secret; the application sources it at runtime (rotation-safe), not via deploy-time
  injection; and the cross-stack `DatabasePasswordSecretArn` export resolves to the managed secret.

### Modified Capabilities

- `reusable-deployment-stack`: the required-secrets contract **removes** `DATABASE_PASSWORD` (the
  reusable workflow no longer accepts or requires a database password secret/parameter).

## Impact

- **Infrastructure:** `infrastructure/db.template` (managed secret + cluster password), `web.template`
  and `api.template` (runtime secret sourcing), `master.template` + `backend.template` (drop the
  parameter).
- **Workflows:** `.github/workflows/deploy.yml` (drop param + required secret),
  `.github/workflows/sample-deploy.yml` (drop preflight check).
- **Application/framework code:** likely `Tjb.Web` / `Tjb.Web.Hosting` (and the sample) to read the
  master secret from Secrets Manager at startup/connection instead of consuming a pre-resolved
  connection string — the rotation-safe path. The ECS task role needs `secretsmanager:GetSecretValue`
  on the managed secret (see `infrastructure/security.template` SES-policy precedent).
- **Migration risk:** enabling `ManageMasterUserPassword` on the **existing** dev/test/app clusters
  rotates the live password and changes the `DatabasePasswordSecretArn` export value while web/api/
  feature stacks import it — CFN blocks changing an in-use export, so the cutover must be sequenced
  (design.md). Production (`app`) is the highest blast radius.
- **Operator/config:** forks and TaskManager no longer set `DATABASE_PASSWORD`; one fewer required
  secret. No new operator action.
- **Out of scope:** changing the DB engine, the `${domain}_admin` username derivation, IAM database
  authentication as the primary mechanism (noted as an alternative in design), and any unrelated
  secrets.
