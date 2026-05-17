## Context

The platform deploys to two AWS regions today:

- **Primary:** `us-east-1` for every branch.
- **Secondary:** `us-west-2`, but only for the three "shared infrastructure" branches `app` / `beta` / `alpha` (which have `IsMultiRegion=true`). Single-region branches (`dev`, all feature branches) deploy only to `us-east-1`.

The Aurora Global Cluster `taskmanager-${BranchName}-global-cluster` for each multi-region branch spans both regions: the primary writer in `us-east-1` and a read replica in `us-west-2`. The KMS keys encrypting those clusters are multi-region keys with their replicas in `us-west-2` — owned by the `bootstrap-prod` / `bootstrap-nonprod` stacks after the prior change [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md).

This change keeps the topology shape unchanged (one primary + one secondary, three multi-region branches, single-region branches unaffected) and only moves the secondary region from `us-west-2` to `us-east-2`. The reason is purely cost — `us-east-2` is cheaper than `us-west-2` for our workload, and we have no latency, compliance, or customer-locality reason to be on the west coast.

The migration constraint that drives most of the sequencing: an **Aurora Global Cluster cannot have two secondary regional clusters simultaneously in different regions**. We have to fully remove the `us-west-2` cluster from the global cluster before we add the `us-east-2` one.

## Goals / Non-Goals

**Goals:**

- Move secondary AWS region from `us-west-2` to `us-east-2` for all multi-region branches.
- Keep the change reversible up to the point of `us-west-2` stack deletion.
- Avoid data loss: `app`/`beta`/`alpha` Aurora data is preserved through the migration via the global cluster's own replication once the new region is added.
- Leave no `us-west-2` literal in the repository when the change is done.

**Non-Goals:**

- **Not** changing the primary region (`us-east-1` stays).
- **Not** changing which branches are multi-region (`app`/`beta`/`alpha` remain so; `dev` and feature branches remain single-region).
- **Not** changing KMS topology, key policy, or bootstrap stack content. Those land in the prior change.
- **Not** changing application code in `src/`.

## Decisions

### D1. Sequence: stand up `us-east-2` first, then tear down `us-west-2`

The natural-feeling sequence would be "delete old, then create new," but that exposes the multi-region branches as effectively single-region during the gap (no failover destination). Better:

1. Verify SES in `us-east-2`.
2. Manually deploy `bootstrap`, `bootstrap-prod`, `bootstrap-nonprod` to `us-east-2` (same operator pattern as the prior change).
3. Confirm SSM parameters resolve in `us-east-2`.
4. For each multi-region branch in order `alpha` → `beta` → `app`: remove the `us-west-2` regional cluster from its global cluster, delete the `us-west-2` env stack, then trigger CI which deploys a new env stack to `us-east-2` that adds its regional cluster to the global cluster.
5. Merge the workflow change (`AWS_REGION_SECONDARY: us-east-2`) only after the manual deploys for at least `alpha` have succeeded — this prevents CI from trying to deploy to `us-east-2` against an empty bootstrap.

Wait — there's a tension here. The workflow change is what tells CI to deploy to `us-east-2`. Until it's merged, CI keeps trying to deploy to `us-west-2`. But if we delete the `us-west-2` env stack first and then a push lands, CI will recreate it in `us-west-2`. Two ways to handle this:

- **(A)** Land the workflow change on a feature branch first, force-merge to `app` last in the migration, and rely on `nodeploy` commit-message gating to suppress accidental redeploys in between. Risky.
- **(B) Chosen approach:** Merge the workflow change early — but before merging, ensure `us-east-2` bootstrap is fully in place. The workflow will then start failing for `us-west-2`-bound logic, but our matrix logic now selects `us-east-2`, so it just deploys there. Old `us-west-2` stacks are deleted asynchronously by the operator after each branch has a healthy `us-east-2` deploy.

The fail-safe with (B) is that the old `us-west-2` env stack continues to run independently until manually deleted — we are paying for both regions briefly, but there's no service outage and no race.

### D2. Use Aurora Global Database's native replication, not snapshot/restore, for data migration

When the `us-east-2` regional cluster is added to the existing global cluster via `aws rds create-db-cluster --global-cluster-identifier taskmanager-${BranchName}-global-cluster --source-region us-east-1 ...` (which the CloudFormation `AWS::RDS::DBCluster` resource does for us when `GlobalClusterIdentifier` is set and `IsPrimaryRegion=false`), Aurora Global Database itself does the initial seeding from the primary. No snapshot dance needed.

This works because we are *adding* a secondary region, not migrating between two single-region clusters. Aurora's documented procedure for "change secondary region of a global database" is precisely: remove old secondary, add new secondary.

**Alternative considered:** Snapshot `us-west-2` cluster, restore in `us-east-2`, then attach to global cluster. Rejected — snapshots can't be restored into a new region as a secondary of an existing global cluster in one step; would require turning off global, restoring, re-establishing.

### D3. Route 53 health checks rebuilt, not edited in place

The current Route 53 health checks and failover records target `us-west-2` ALB DNS names. The new `us-east-2` ALBs have different DNS names. CloudFormation's `AWS::Route53::HealthCheck` and `AWS::Route53::RecordSet` resources are part of `application.template` (DNS section), which is laid down per region. As `us-east-2` env stacks are deployed, they create the new health checks; once `us-west-2` stacks are deleted, the old health checks are gone with them.

The brief window where both exist is fine — Route 53 will just have two health checks per record set during the cutover, and the failover routing policy works correctly either way.

### D4. SES production access is independent per region

Per [CLAUDE.md](../../../CLAUDE.md), SES production-access status is per-region. The new `us-east-2` SES identity will start in sandbox (200 emails/day, recipients must also be verified). We submit the production-access request as part of Phase 0, but it can take ~24h for AWS to approve.

**Practical implication:** between deploying `us-east-2` env stacks and SES production-access being granted, confirmation emails sent from a `us-east-2` ECS task would fail unless the recipient is in the sandbox-verified list. But — emails are sent only from the primary writer's ECS service, which is in `us-east-1`. The `us-east-2` ECS service exists for failover only and is normally idle (no scheduled traffic). So we are not blocked on SES production access for the standard happy path; we are only blocked if an `us-east-1` outage forces failover to `us-east-2` during the SES-sandbox window. Document this exposure window.

### D5. Update CLAUDE.md and BRANCH_MANAGEMENT_README.md as part of the change, not after

The repo docs are the only authoritative pointer to operational facts like "secondary is us-west-2" and "SES verified in both regions." Updating them in the same PR as the workflow change ensures they don't drift.

## Risks / Trade-offs

- **[Risk] Aurora Global Cluster transient state when removing the `us-west-2` member.** → Mitigate: `aws rds remove-from-global-cluster` is a documented single-call op; it leaves the standalone `us-west-2` cluster intact (which we then delete via stack delete). The global cluster stays writable from the primary the whole time.
- **[Risk] Race between workflow change and CI redeploying `us-west-2`.** → Mitigate per D1: deploy `us-east-2` bootstrap first; merge workflow change; old `us-west-2` env stacks are deleted asynchronously per branch by operator after `us-east-2` is healthy.
- **[Risk] DNS failover behaves unexpectedly during the dual-region window.** → Mitigate: Route 53 health checks and failover records during the dual window are valid (two healthy records, one primary, one secondary; failover policy picks the active one). We rely on documented Route 53 semantics rather than custom logic.
- **[Risk] SES sandbox in `us-east-2` blocks failover emails.** → Mitigate: failover is an outage event, and we're consciously accepting a brief window (Phase 0–3) where outbound mail from `us-east-2` is sandbox-only. Document the window and target completion of SES production-access before declaring the migration "done."
- **[Risk] Lingering `us-west-2` AWS spend if operator forgets to delete the old stacks.** → Mitigate: Phase 4 cleanup checklist explicitly lists every `us-west-2` stack to delete; the change is not archived until every item is checked.
- **[Trade-off] Brief period of paying for both regions** — accepted as the cost of safe migration. Estimated cost: ~$30–80/day during the migration window.

## Migration Plan

**Phase 0 — SES prereqs (manual, ~24h elapsed including AWS turnaround)**
1. `aws sesv2 create-email-identity --email-identity appcloud.systems --region us-east-2`.
2. Add the three returned DKIM CNAMEs to Route 53 hosted zone `Z06422172SASV44F5Y8VA`.
3. Verify `DkimAttributes.Status: SUCCESS` for the `us-east-2` identity.
4. Submit `aws sesv2 put-account-details --production-access-enabled --region us-east-2`.

**Phase 1 — Bootstrap stand-up in `us-east-2`**
5. Human admin deploys `bootstrap` to `us-east-2` (mirrors `us-east-1` operator pattern from the prior change).
6. Human admin deploys `bootstrap-nonprod` to `us-east-2`. `PrimaryKeyArn` = the existing `bootstrap-nonprod` key in `us-east-1`.
7. Human admin deploys `bootstrap-prod` to `us-east-2`. `PrimaryKeyArn` = the existing `bootstrap-prod` key in `us-east-1`.
8. Verify SSM parameters `/taskmanager/kms/{prod,nonprod}/aurora-key-arn` exist in `us-east-2` and resolve to valid replica ARNs.

**Phase 2 — Workflow change**
9. Update [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml): `AWS_REGION_SECONDARY: us-east-2`; matrix `region: us-east-2`. Grep the workflow for `us-west-2` literals and replace.
10. Update [CLAUDE.md](../../../CLAUDE.md) and [BRANCH_MANAGEMENT_README.md](../../../BRANCH_MANAGEMENT_README.md) accordingly.
11. Open PR, merge to `app`. Default-branch-only event handlers (per the existing `feedback_github_actions_default_branch_events` memory) will pick up the new region.

**Phase 3 — Per-branch cutover, lowest criticality first**
12. **`alpha` first.**
    - Snapshot `alpha`'s `us-west-2` cluster (just-in-case): `pre-region-shift-alpha-uswest2-<YYYY-MM-DD>`.
    - Remove `us-west-2` cluster from global cluster: `aws rds remove-from-global-cluster --global-cluster-identifier taskmanager-alpha-global-cluster --db-cluster-identifier <usw2-arn>`.
    - Delete `alpha`'s `us-west-2` env stack: `aws cloudformation delete-stack --stack-name alpha-appcloud-systems --region us-west-2`.
    - Trigger CI on `alpha` (empty push or workflow re-run); verify the deploy creates `us-east-2` env stack and adds the new regional cluster to the global cluster.
    - Verify `aws rds describe-global-clusters --global-cluster-identifier taskmanager-alpha-global-cluster` shows two members in `us-east-1` and `us-east-2`.
13. **`beta`.** Same recipe.
14. **`app` (production).** Same recipe — but schedule a maintenance window and notify stakeholders. The cutover itself is no-downtime (the primary in `us-east-1` is unaffected), but the global-cluster reshape is the most production-touching operation in the change.

**Phase 4 — Cleanup**
15. Delete `bootstrap-prod` in `us-west-2`: `aws cloudformation delete-stack --stack-name bootstrap-prod --region us-west-2`. The KMS replica is removed (CloudFormation deletes the `AWS::KMS::ReplicaKey` resource — `PendingWindowInDays: 7` applies, key continues to bill for 7 days then is gone).
16. Delete `bootstrap-nonprod` in `us-west-2`. Same.
17. Delete `bootstrap` in `us-west-2`. The ECR repo's images are abandoned (no lifecycle issue since the ECR repo itself is being deleted).
18. Verify no `us-west-2` CloudFormation stacks remain: `aws cloudformation list-stacks --region us-west-2 --stack-status-filter CREATE_COMPLETE UPDATE_COMPLETE` returns empty.

**Rollback strategies:**
- Phases 0–1: no production impact; just clean up the `us-east-2` SES identity and bootstrap stacks.
- Phase 2: revert the workflow + docs commit on `app`. CI starts deploying to `us-west-2` again. Bootstrap in `us-west-2` was never deleted (Phase 4) so existing `us-west-2` env stacks would just be re-deployed/updated.
- Phase 3 per branch: re-create the `us-west-2` env stack from git history (or by checking out an earlier commit and triggering CI); re-add to global cluster via `aws rds`. Slow but mechanical.
- Phase 4 is the point of no return for the region change.

## Open Questions

_None known._ All open questions from the prior change were resolved before this change was scoped.
