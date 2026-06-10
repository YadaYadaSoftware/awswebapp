## MODIFIED Requirements

### Requirement: Input/secret surface is explicitly typed and documented

The reusable workflow SHALL declare every project-specific value as either an `input` (in the `on: workflow_call: inputs:` block) or a `secret` (in `on: workflow_call: secrets:`). The workflow SHALL fail loudly at run time if any required input or secret is missing.

The required inputs SHALL include at minimum: `branch-name`, `environment`, `custom-version`, `domain-name`, `hosted-zone-id`, `region-primary`, `region-secondary`, `multi-region-branches`, `shared-infra-branches`, `prod-branch`, `dotnet-version`, `web-dockerfile-path`. (Per the §0.1 derive-from-domain decision there is **no** `project-name` input — `domain-name` is the sole naming input.)

The required secrets SHALL include at minimum: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_ACCESS_KEY_ID_PROD`, `AWS_SECRET_ACCESS_KEY_PROD`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`. The workflow SHALL NOT require or consume a `DATABASE_PASSWORD` secret — the Aurora master password is RDS-managed (see the `rds-managed-db-password` capability), so no database password is supplied through the pipeline.

Every input SHALL have a sensible default OR be marked `required: true`. The README/`CONSUMING.md` SHALL document each input's purpose, type, default, and example value.

#### Scenario: Required input missing
- **WHEN** a consumer calls the reusable workflow without providing the `domain-name` input
- **THEN** GitHub Actions rejects the workflow at parse time, before any job runs, with a clear error naming the missing input

#### Scenario: Required secret missing
- **WHEN** a consumer calls the workflow with all inputs but without setting the `AWS_ACCESS_KEY_ID` secret
- **THEN** the existing "Select AWS credentials by branch" step's empty-secret guard fires and fails the workflow with a clear message identifying the missing secret

#### Scenario: No database password secret is consumed
- **WHEN** a consumer calls the reusable workflow without a `DATABASE_PASSWORD` secret
- **THEN** the workflow neither requires nor references it, and the deploy provisions Aurora with an RDS-managed master password
