## Why

CloudFormation **export names must be unique per account per region**. Every cross-stack export in this infrastructure is named `<Name>-${BranchName}` (e.g. `DatabaseHost-dev`, `VPCId-app`, `WebEndpoint-beta`) — the branch leaf is in the name, but the **deployment domain is not**. That breaks the otherwise domain-agnostic property the rest of the stack maintains (see CLAUDE.md "Infrastructure naming convention", where SSM paths, IAM scopes, bucket names, and cluster IDs all derive from the dashed domain).

Today only `appcloud.systems` is deployed, so the collision is **latent**. The moment a second domain (e.g. `example.com` → `example-com`) deploys a `dev`/`app`/etc. stack into the **same account+region**, its backend would try to publish `DatabaseHost-dev` (and ~32 others) — names already owned by the `appcloud.systems` deployment — and the deploy fails with `Export with name … is already exported by stack …`. The pipeline is advertised as domain-and-region-agnostic; this is the one place that quietly isn't.

This surfaced during `robust-aurora-cluster-teardown`: the `Cannot update export DatabaseHost-dev … in use by …` rollback made the un-qualified export name visible and prompted the question "shouldn't the domain be in the export?". (Note: domain-qualifying the export name does **not** fix that rollback — that was a value-change-while-imported problem, already fixed by dropping the cluster rename. This change is purely about cross-domain name-collision safety / convention consistency.)

## What Changes

- **Qualify every cross-stack export name** with the dashed deployment domain: `<Name>-${BranchName}` → `<Name>-${BranchName}-${DomainDashed}` (e.g. `DatabaseHost-dev-appcloud-systems`). `${DomainDashed}` is derived locally in each template from the existing `DomainName` parameter via `!Join ["-", !Split [".", !Ref DomainName]]` — no new parameter, matching the established convention.
- **Repoint every importer** (`Fn::ImportValue: !Sub "<Name>-${EnvironmentToImport}"` → `… -${EnvironmentToImport}-${DomainDashed}"`) so consumers resolve the new names.
- Because **an export that is currently imported cannot be renamed or removed in place** (CloudFormation rejects it with the same "in use" error), this is executed as a **3-phase migration**, not a single rename:
  1. **Add** the new domain-qualified exports *alongside* the existing ones (both carry the same value). Deploy backend everywhere.
  2. **Repoint** all importers to the new names. Deploy every consumer stack (Web, Api, Dns, Db cross-imports — including every feature-branch app stack, which imports dev's backend exports).
  3. **Remove** the old un-qualified exports. Deploy backend everywhere.

## Capabilities

### New Capabilities

- `cross-stack-export-naming`: the naming contract for every CloudFormation `Export` and `Fn::ImportValue` across the infrastructure templates — that names are qualified by both branch leaf and dashed deployment domain so two domains can coexist in one account+region — and the phased migration rule (never rename/remove an in-use export in place).

### Modified Capabilities

_None as separate spec docs._ This is orthogonal to `aurora-cluster-teardown` and `aurora-kms-key-management`; it touches export/import wiring only, not resource behavior.

## Impact

**Infrastructure templates (all that export or import):**
- Exporters (add domain suffix to ~33 `Export.Name`s): [db.template](../../../infrastructure/db.template), [web.template](../../../infrastructure/web.template), [api.template](../../../infrastructure/api.template), [network.template](../../../infrastructure/network.template), [infrastructure.template](../../../infrastructure/infrastructure.template), [security.template](../../../infrastructure/security.template).
- Importers (add domain suffix to ~30 `Fn::ImportValue`s): [api.template](../../../infrastructure/api.template), [db.template](../../../infrastructure/db.template), [dns.template](../../../infrastructure/dns.template), [web.template](../../../infrastructure/web.template).
- `EnvironmentToImport` plumbing (master/backend/application) is unchanged — only the import *string* gains the domain suffix; each importing template derives `${DomainDashed}` from its own `DomainName`.

**Deploy sequencing:** three ordered deploy waves (add → repoint → remove). Between phases the stacks are fully consistent (phase 1 leaves both names valid; phase 3 only removes names no longer imported). No data resources are touched; no Aurora replacement; no downtime.

**Out of scope (explicitly):**
- The value/shape of any export (only the `Name` changes).
- `EnvironmentToImport` selection logic and the shared-vs-feature-branch import architecture.
- Non-export resource naming (already domain-derived elsewhere).
