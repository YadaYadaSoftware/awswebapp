# VS Code MySQL Setup Guide

> Filename note: this file is named `VSCODE_POSTGRESQL_SETUP.md` for legacy reasons.
> The database is **MySQL** (Aurora MySQL, port 3306), not PostgreSQL. The content
> below is for MySQL.

## Overview

This guide shows how to query a **local** MySQL instance from VS Code during
development. The deployed Aurora MySQL cluster is **not** reachable from a local
client — query it through the AWS Console RDS Query Editor instead (see
[DATABASE_ACCESS_GUIDE.md](DATABASE_ACCESS_GUIDE.md)).

## Pick a VS Code extension

Use a **MySQL-compatible** SQL extension/driver:

- **SQLTools** (`mtxr.sqltools`) + **SQLTools MySQL/MariaDB driver**
  (`mtxr.sqltools-driver-mysql`) — recommended.
- Or any general MySQL client extension (e.g. "MySQL" by Weijan Chen).

A PostgreSQL extension/driver will **not** connect to this database.

## Local MySQL (development)

1. Run MySQL on `localhost:3306`.
2. Apply migrations and seed: `dotnet run --project src/Tjb.Migrations`.
3. Configure the connection (see SQLTools settings below) pointing at `localhost:3306`.

The `appsettings.json` connection strings are localhost defaults
(`Server=localhost;Database=TjbDb;...`).

## Deployed Aurora MySQL

You **cannot** point VS Code (or any local client) at the deployed cluster — it is
private, with no bastion, tunnel, or RDS Proxy. Query the deployed database through
the **AWS Console → RDS → Query Editor**, authenticating with the cluster's Secrets
Manager password secret. See [DATABASE_ACCESS_GUIDE.md](DATABASE_ACCESS_GUIDE.md) for
the full procedure (cluster, secret path, username, database name).

## SQLTools connection settings

Create `.vscode/settings.json` (do not commit real passwords — use `askForPassword`):

```json
{
  "sqltools.connections": [
    {
      "name": "Tjb MySQL",
      "driver": "MySQL",
      "server": "localhost",
      "port": 3306,
      "database": "",
      "username": "",
      "askForPassword": true,
      "connectionTimeout": 30
    }
  ]
}
```

Leave `database` blank to browse all schemas, or set it to your local application
database name (`TjbDb` by default, per `appsettings.json`).

## Test the connection

```sql
SELECT VERSION();
SHOW DATABASES;
```

## Useful queries

The schema (see
[../Tjb.Migrations/20251007185200_InitialCreate.cs](../Tjb.Migrations/20251007185200_InitialCreate.cs))
has application tables `Users`, `Projects`, `Tasks`, `ProjectMembers`, `Invitations`,
plus the ASP.NET Identity `AspNet*` tables (Identity shares this database).

```sql
-- EF Core migration history
SELECT * FROM `__EFMigrationsHistory`;

-- Users
SELECT Id, Email, FirstName, LastName, CreatedAt FROM `Users`;

-- Projects with owners
SELECT p.Name, p.Description, u.Email AS owner_email
FROM `Projects` p
JOIN `Users` u ON p.OwnerId = u.Id;

-- Tasks with assignees
SELECT t.Title, t.Status, t.Priority, u.Email AS assigned_to
FROM `Tasks` t
LEFT JOIN `Users` u ON t.AssignedToId = u.Id;

-- Project memberships
SELECT p.Name AS project, u.Email AS member, pm.Role
FROM `ProjectMembers` pm
JOIN `Projects` p ON pm.ProjectId = p.Id
JOIN `Users` u ON pm.UserId = u.Id;
```

> Note: `Status`, `Priority`, and `Role` are stored as integers (the enums
> `TaskStatus`, `TaskPriority`, `ProjectRole` in `Tjb.Shared`).

## Database administration (MySQL system queries)

Use MySQL's `information_schema` / `SHOW` commands (not the PostgreSQL `pg_*`
catalogs):

```sql
-- List tables in the current database
SHOW TABLES;

-- Table sizes
SELECT table_name,
       ROUND((data_length + index_length) / 1024 / 1024, 2) AS size_mb
FROM information_schema.tables
WHERE table_schema = DATABASE()
ORDER BY (data_length + index_length) DESC;

-- Active connections
SHOW PROCESSLIST;
```

## Troubleshooting

- **Connection refused**: ensure local MySQL is up. Verify with
  `Test-NetConnection localhost -Port 3306`.
- **Access denied**: confirm your local MySQL username/password; test with
  `mysql -h localhost -P 3306 -u <username> -p` first.
- **Driver mismatch**: confirm you installed a **MySQL** driver, not PostgreSQL.
