## ADDED Requirements

### Requirement: Aurora master password is RDS-managed

The Aurora cluster SHALL use an RDS-managed master user password (`ManageMasterUserPassword: true`)
rather than an operator-supplied value. The deployment pipeline SHALL NOT pass a `DatabasePassword`
CloudFormation parameter and SHALL NOT define a self-managed Secrets Manager secret containing the
password. RDS SHALL generate, store, and rotate the password in its managed Secrets Manager secret,
encrypted with the deployment's existing Aurora KMS key. The `MasterUsername` derivation
(`${domain}_admin`) is unchanged.

#### Scenario: Cluster created without an external password
- **WHEN** a master-template (shared-infra) branch is deployed
- **THEN** the Aurora cluster is created with `ManageMasterUserPassword` enabled, no `MasterUserPassword` is supplied, and no `DatabasePassword` parameter is required by the deploy

#### Scenario: Managed secret is encrypted with the Aurora KMS key
- **WHEN** the RDS-managed master secret is created
- **THEN** it is encrypted with the deployment's existing Aurora KMS key (not the default AWS-managed key)

### Requirement: The application sources the master password at runtime, rotation-safe

The application SHALL obtain the database password by reading the RDS-managed Secrets Manager secret
**at runtime**, not by resolving it into a static container environment value at deploy time. The
application SHALL recover from a master-password rotation without redeployment — at minimum by
re-reading the secret when a database authentication failure occurs. The connection string's
non-secret parts (host, database name, username) MAY continue to come from cross-stack imports.

#### Scenario: Password read at startup from the managed secret
- **WHEN** the web container starts
- **THEN** it reads the password from the RDS-managed secret (JSON `password` field) and connects, without the password being baked into the task definition

#### Scenario: Rotation does not break a running app
- **WHEN** RDS rotates the master password
- **THEN** the application continues to authenticate — re-reading the managed secret on an auth failure — without a manual redeploy

### Requirement: Managed secret ARN is exposed via a cross-stack export

The backend stack SHALL export the RDS-managed master secret's ARN under a domain-and-branch-qualified
export name so that web, api, and feature-branch stacks can resolve the password source. Introducing
the managed-secret export and retiring the prior self-managed-secret export SHALL be sequenced so that
no CloudFormation export value changes while it is still imported by another stack.

#### Scenario: Consumer stack resolves the managed secret
- **WHEN** a web/api/feature stack deploys
- **THEN** it imports the managed master secret ARN export for its environment and domain and uses it as the runtime password source

#### Scenario: Export cutover does not break in-use imports
- **WHEN** the deployment migrates from the self-managed secret export to the managed-secret export
- **THEN** the new export is published and consumers migrate to it before the old export is removed, so no in-use export value is changed in place

### Requirement: No DATABASE_PASSWORD secret in the deployment pipeline

The reusable deploy workflow and its callers SHALL NOT require or consume a `DATABASE_PASSWORD` GitHub
secret. The `sample-deploy.yml` config-validation preflight SHALL NOT list `DATABASE_PASSWORD`, and the
onboarding documentation/checklists SHALL NOT instruct setting it.

#### Scenario: Deploy runs without DATABASE_PASSWORD
- **WHEN** a repository deploys with no `DATABASE_PASSWORD` secret set
- **THEN** the preflight passes (it does not list the secret) and the deploy provisions the cluster with the RDS-managed password

#### Scenario: Onboarding checklist omits the secret
- **WHEN** a fork operator follows the setup docs and the config-validation preflight
- **THEN** neither lists `DATABASE_PASSWORD` among required secrets
