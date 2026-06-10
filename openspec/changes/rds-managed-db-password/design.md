## Context

Today the Aurora master credential is operator-supplied and lives in three places:

- The `DATABASE_PASSWORD` GitHub secret → passed as the `DatabasePassword` CFN parameter on
  master-template (shared-infra) deploys only (`deploy.yml` ~501; threaded master→backend→db).
- `db.template` sets the cluster's `MasterUserPassword: !If [IsPrimary, !Ref DatabasePassword, …]`
  (primary member only; the global secondary uses `AWS::NoValue`).
- `db.template` also creates a **self-managed** `AWS::SecretsManager::Secret` whose `SecretString` is
  the raw password (`!Ref DatabasePassword`), exported as `DatabasePasswordSecretArn-${Branch}-${DomainDashed}`.
- `web.template`/`api.template` build `ConnectionStrings__DefaultConnection` **at deploy time** via
  `Password={{resolve:secretsmanager:<imported ARN>:SecretString}}` — the plaintext password is baked
  into the ECS task-definition env var. There is no rotation.

`ManageMasterUserPassword: true` lets RDS generate, store, and rotate the password in a managed
Secrets Manager secret (JSON `{username, password}`), removing the GitHub secret, the CFN parameter,
and the hand-rolled secret resource. The complication is that RDS-managed secrets **auto-rotate
(~7 days by default)**, which is fundamentally incompatible with baking the password into a static env
var at deploy time.

## Goals / Non-Goals

**Goals:**
- Remove the `DATABASE_PASSWORD` GitHub secret and the `DatabasePassword` CFN parameter entirely.
- RDS owns generation + rotation of the master password in a managed Secrets Manager secret.
- The application reads the password in a **rotation-safe** way (a 7-day rotation never causes auth
  failures on a running task).
- Keep the change domain-derived and multi-region-correct; no regressions for TaskManager or the fork.

**Non-Goals:**
- Switching to IAM database authentication (token-based, passwordless) — cleaner long-term but a much
  larger app/EF/Pomelo change; noted as a considered alternative, not adopted here.
- Changing the DB engine, the `${domain}_admin` username derivation, or unrelated secrets.
- Building a custom rotation Lambda (RDS-managed rotation replaces the need).

## Decisions

### D1. Enable `ManageMasterUserPassword` on the primary cluster; reuse the Aurora KMS key
`db.template` `AuroraCluster` gains `ManageMasterUserPassword: true` and (under `IsPrimary`)
`MasterUserSecret.KmsKeyId: !Ref AuroraKmsKeyArn`; `MasterUserPassword` is removed; `MasterUsername`
stays. The secondary global member keeps `AWS::NoValue` (it shares the primary's credential).
*Alternatives:* keep self-managed secret + a custom rotation Lambda (more code, more IAM, reinvents
RDS feature) — rejected.

### D2. Source the password at RUNTIME from the managed secret, not deploy-time `{{resolve}}` (central decision)
Because the managed secret rotates, the deploy-time injection must go. The web/api stacks stop
resolving the password into the connection string; instead they pass the **managed secret ARN** to the
container (e.g. env var `DB_SECRET_ARN`), and the app/framework reads the secret JSON at startup and
**re-fetches on an authentication failure** (so a rotation that lands between task starts self-heals).
This lands in `Tjb.Web.Hosting` (a connection-string provider) so the sample and TaskManager both
inherit it; the host keeps building `Server=…;Database=…;User Id=…` from the existing
`DatabaseHost`/`DatabaseName`/`DatabaseUsername` imports and injects the password from the secret.
*Alternatives considered:*
- **IAM DB auth** — passwordless, most secure; rejected as out-of-scope (token lifecycle + Pomelo
  integration is its own change).
- **Keep `{{resolve}}` and disable rotation** — RDS-managed rotation can't be cleanly disabled and the
  whole point is rotation; rejected.
- **Read once at startup only (no re-fetch)** — simpler, but a rotation during a long-lived task plus
  any reconnect would fail; rejected in favor of re-fetch-on-auth-failure.

### D3. Cutover the cross-stack export with a NEW export name (avoid "export in use")
CFN refuses to change an `Export.Value` while another stack imports it, and the
`DatabasePasswordSecretArn` value changes (self-managed ARN → managed ARN). So introduce a new export
**`DatabaseMasterSecretArn-${Branch}-${DomainDashed}`** for the managed secret, migrate web/api/feature
consumers to import the new name, then remove the old `DatabasePasswordSecret` resource + its export in
a follow-up deploy. This follows the `cross-stack-export-naming` capability and the recorded
two-consumption-path rule (canonical `GetAtt` value, controlled `Export.Name`).
*Alternative:* reuse the same export name and force a value change — blocked by CFN while imported;
rejected.

### D4. ECS task role gains `secretsmanager:GetSecretValue` + `kms:Decrypt`
Runtime retrieval (D2) requires the ECS task role (the `SharedLambdaExecutionRole` / task role in
`infrastructure/security.template`) to read the managed secret and decrypt with the Aurora KMS key —
scoped to the managed secret ARN / that key. Precedent: the existing `SesAccess` policy on the same
role.

## Risks / Trade-offs

- **[Risk] Secondary-region (global cluster) has no local managed secret.** `ManageMasterUserPassword`
  creates the secret only on the primary global member, in the primary region; the secondary region's
  web stack imports from its OWN region's backend. → Replicate the managed secret into the secondary
  region (Secrets Manager **replica secret**) and have the secondary web read the replica, OR have the
  secondary region read the primary secret cross-region with the region pinned. **Must be verified on
  `test` (multi-region) before `app`.** Tracked in Open Questions.
- **[Risk] Enabling on an existing live cluster rotates the password immediately.** `ModifyDBCluster`
  with `ManageMasterUserPassword=true` generates a new secret and changes the password. → The app must
  already read from the managed secret before/at the same deploy; sequence via the Migration Plan, on
  dev → test → app.
- **[Risk] Rollback is asymmetric.** Disabling `ManageMasterUserPassword` again requires supplying a
  `MasterUserPassword` — you can't "un-rotate" back to the old secret. → Treat dev/test as the proving
  ground; for `app`, snapshot first and rehearse the rollback (re-introduce a temporary password) on
  test.
- **[Trade-off] Runtime secret read adds a startup dependency on Secrets Manager.** A Secrets Manager
  outage delays cold start. → Cache the resolved value; fail fast with a clear log (mirrors the
  existing migrate-on-startup tolerance).
- **[Risk] Docs/preflight drift with the in-flight `standalone-fork-readiness` change.** Its preflight
  + checklists list `DATABASE_PASSWORD` required. → This change MUST update them in lockstep (it's a
  listed task), and they should be reconciled at whichever change lands second.

## Migration Plan

Per environment, in order **dev → test → app** (test is the multi-region rehearsal for `app`):

1. **App/framework first.** Ship the `Tjb.Web.Hosting` runtime secret-resolver (D2) and the task-role
   IAM (D4), still pointing at the *existing* self-managed secret ARN (raw string). Verify the app
   reads the password at runtime with no behavior change.
2. **Add the managed secret (new export).** Set `ManageMasterUserPassword` on the cluster and publish
   the new `DatabaseMasterSecretArn` export (D1, D3) — old self-managed secret/export still present.
3. **Point consumers at the managed secret.** Web/api/feature stacks import `DatabaseMasterSecretArn`;
   confirm runtime read of the JSON `password` works (and the secondary-region story from Open
   Questions).
4. **Remove the old wiring.** Drop `DatabasePassword` param threading (db/backend/master), the
   `DatabasePassword=` line + `DATABASE_PASSWORD` required secret in `deploy.yml`, the self-managed
   `DatabasePasswordSecret` + its old export, and clean the preflight + all docs/specs.
5. **Verify multi-region on test, then app.** Confirm both regions authenticate post-rotation (force a
   rotation on test to prove re-fetch-on-auth-failure).

**Rollback:** revert the template/workflow commits; re-supply a `MasterUserPassword` (temporary) on the
cluster if `ManageMasterUserPassword` was already enabled (it cannot silently revert). Rehearse on test.

## Open Questions

1. **Secondary-region secret delivery (blocking for multi-region).** Replica secret vs. cross-region
   read — decide and validate on `test` before promoting to `app`. Does Aurora global secondary expose
   any managed secret, or must we replicate the primary's?
2. **Rotation pickup.** Confirm RDS keeps the previous password valid through the rotation window long
   enough that re-fetch-on-auth-failure (D2) always succeeds; decide whether to also pin a longer
   rotation interval initially.
3. **Sample vs TaskManager lockstep.** The framework resolver (D2) must ship in a published
   `Tjb.Web.Hosting` version the sample consumes before the sample's templates flip — sequence against
   the framework package publish.
