# Database Access Guide - Aurora MySQL (private VPC)

## Overview

The deployed database is **Aurora MySQL Serverless v2** (port 3306), running in
private subnets and never exposed to the internet. The shared-infrastructure
branches (`app`, `beta`, `alpha`) run an Aurora Global Cluster; `dev` is
single-region. Defined in
[../../infrastructure/db.template](../../infrastructure/db.template).

There is no bastion host, no SSH/SSM tunnel, no RDS Proxy, and no way to point a
local desktop client at the deployed cluster. **To query the deployed database,
use the AWS Console RDS Query Editor** (browser-based). For local development you
run your own MySQL on `localhost` (see "Local development" below).

## Querying the deployed database: AWS RDS Query Editor

The Aurora clusters for `dev`/`beta`/`alpha` have the RDS Data API HTTP endpoint
enabled (`EnableHttpEndpoint` in
[../../infrastructure/db.template](../../infrastructure/db.template)), which is
what the Query Editor uses.

1. Sign in to the **AWS Console** and go to **RDS → Query Editor**
   (in the region the env is deployed to — e.g. `us-east-1` for `dev`).
2. **Database instance/cluster**: pick the Aurora cluster for the branch you
   want. The cluster is auto-named by CloudFormation, so identify it by the
   stack it belongs to (see "Naming" below) or by the `DatabaseEndpoint` /
   `DatabaseClusterArn` stack output.
3. **Authentication**: choose **Secrets Manager ARN** and supply the cluster's
   password secret (recommended — see "Credentials" below), or choose username +
   password and enter the master username and the password from that secret.
4. **Database name**: enter the database name for the branch — read it from the
   stack's `DatabaseName` output (the deployed schema name; the application's
   tables and the ASP.NET Identity `AspNet*` tables share it).
5. Run SQL in the editor, e.g.:

   ```sql
   SELECT * FROM `__EFMigrationsHistory`;
   SHOW TABLES;
   ```

## Credentials (AWS Secrets Manager)

The DB password is stored per branch/region. The secret path pattern (from
[../../infrastructure/db.template](../../infrastructure/db.template)) is:

```
{dashed-domain}/database/{branch}/regional/{region}/password
```

For example, for domain `appcloud.systems` on branch `dev` in `us-east-1`:
`appcloud-systems/database/dev/regional/us-east-1/password`.

The MySQL master username is derived (MySQL disallows hyphens), e.g.
`appcloud_systems_admin` for `appcloud.systems`. Prefer authenticating the Query
Editor with the secret ARN directly rather than copying the password.

To find the secret ARN, endpoint, and database name from a deployed stack:

```bash
# Stack name follows the {branch-leaf}-{dashed-domain} pattern, e.g. dev-appcloud-systems
aws cloudformation describe-stacks \
  --stack-name dev-appcloud-systems \
  --query 'Stacks[0].Outputs[?OutputKey==`DatabasePasswordSecretArn` || OutputKey==`DatabaseEndpoint` || OutputKey==`DatabaseName`].[OutputKey,OutputValue]' \
  --output table
```

The deployed container reads this same secret via CloudFormation — see the
`ConnectionStrings__DefaultConnection` environment variable in
[../../infrastructure/web.template](../../infrastructure/web.template), which
resolves `DatabasePasswordSecretArn` from Secrets Manager.

## Naming

- Per-branch CloudFormation stacks are named `{branch-leaf}-{dashed-domain}`
  (e.g. `dev-appcloud-systems`).
- Aurora cluster IDs and the global-cluster ID derive from the dashed domain and
  branch; the per-region cluster is CFN-auto-named, so locate it via the stack's
  `DatabaseEndpoint` / `DatabaseClusterArn` outputs rather than guessing a name.
- Endpoints, the username, and the password secret ARN are exported by the
  backend stack and imported by the app stack; prefer reading the stack outputs /
  secret over hardcoding.

## Local development

For local work, run MySQL on `localhost:3306` and apply migrations. The
`appsettings.json` connection strings are localhost defaults
(`Server=localhost;Database=TjbDb;...`); real deployed values come from Secrets
Manager.

```bash
# Install MySQL locally
# Windows: download MySQL Community Server from dev.mysql.com
# macOS: brew install mysql
# Linux: sudo apt-get install mysql-server

# Apply migrations + seed against local MySQL
dotnet run --project src/Tjb.Migrations
```

You can point any local MySQL client (CLI, MySQL Workbench, DBeaver, VS Code
SQLTools) at `localhost:3306` for local-dev work — see
[VSCODE_POSTGRESQL_SETUP.md](VSCODE_POSTGRESQL_SETUP.md). The deployed cluster is
queried only through the AWS RDS Query Editor.

## Monitoring and logging

- **RDS Performance Insights / CloudWatch Metrics** — CPU, connections, storage.
- **CloudWatch Logs** — Aurora exports `audit`, `error`, `general`, `slowquery`;
  plus ECS/container migration and application logs.
- **CloudTrail** — audits database and Secrets Manager access.
