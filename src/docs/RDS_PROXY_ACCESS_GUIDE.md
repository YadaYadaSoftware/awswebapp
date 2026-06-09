# Deployed Database Access (no RDS Proxy)

> Filename note: this file is named for an "RDS Proxy" design that was never
> built. There is **no RDS Proxy** in any infrastructure template, and there is
> no bastion host, SSH tunnel, or SSM port-forward to the deployed database.

The deployed database is **Aurora MySQL Serverless v2** (port 3306) in private
subnets, defined in [../../infrastructure/db.template](../../infrastructure/db.template).

To query the deployed database, use the **AWS Console → RDS → Query Editor**
(browser-based), authenticating with the cluster's Secrets Manager password
secret. The full procedure — finding the cluster, the secret path, the master
username, and the database name — is in
[DATABASE_ACCESS_GUIDE.md](DATABASE_ACCESS_GUIDE.md).

For **local development** against a local MySQL on `localhost:3306`, see
[DATABASE_ACCESS_GUIDE.md](DATABASE_ACCESS_GUIDE.md) and
[VSCODE_POSTGRESQL_SETUP.md](VSCODE_POSTGRESQL_SETUP.md).
