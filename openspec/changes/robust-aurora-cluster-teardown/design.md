## Context

Today, env-stack deletes use CFN's built-in `AWS::RDS::DBCluster` delete handler. Its behavior is roughly:

1. CFN calls `rds:DeleteDBCluster` with `SkipFinalSnapshot=true`.
2. RDS validates the request — and rejects it if the cluster is in any non-deletable state (`backing-up`, `creating`, `modifying`, `inaccessible-encryption-credentials`, `failed`, `incompatible-restore`, etc.).
3. If rejected, CFN gives up on the cluster delete. It then tries to delete dependent resources (`AuroraDBSubnetGroup`, security groups, etc.).
4. Those deletes fail because the cluster still exists and still references them.
5. Stack ends in `DELETE_FAILED`. No further CFN automation can recover it without operator intervention.

This isn't a hypothetical edge case — it took down the `app-appcloud-systems` deploy during the `centralize-aurora-kms-keys` rollout (five failed delete-stack attempts over 16 days; recovery required `aws rds delete-db-cluster --skip-final-snapshot` plus a `RevokeGrant` call, then re-running `delete-stack`). The triggering KMS bug is now fixed in the template, but the same deadlock shape will recur from any future cause that leaves a cluster in a transient state.

The fix has to live in the stack itself because operator-triggered cleanup doesn't catch every deletion path (workflow-triggered deletes, branch-delete cleanup, CFN rollback after failed CREATE, manual `delete-stack` from a developer terminal). A CloudFormation custom resource is the right shape — it participates in the stack lifecycle automatically.

## Goals / Non-Goals

**Goals:**

- Make Aurora env-stack deletes reliable: a healthy cluster delete cleanly, and a *stuck* cluster gets forced through deletion before the rest of the stack tries to tear down.
- Cover all deletion entry points: branch-delete workflow, manual `delete-stack`, CFN rollback after failed CREATE.
- Surface AWS-side rejections clearly (if the API genuinely cannot delete the cluster — e.g., it's in a state AWS hasn't documented a recovery for — log the reason and let CFN report a clean `DELETE_FAILED` *with* the reason, not the misleading "subnet group is still using it" downstream symptom).
- Zero behavior change on `Create` / `Update` deploys. The custom resource is a no-op at deploy time.

**Non-Goals:**

- Not graceful shutdown with snapshot. The handler always passes `--skip-final-snapshot`. Operators who want a snapshot before tearing down should do it out-of-band first.
- Not generic to all stuck AWS resources. Aurora cluster only. ENI-pinned-by-Fargate-task, ALB-with-active-connections, etc., are their own problems with their own handlers.
- Not a replacement for CFN's normal delete path. The custom resource only kicks in if the cluster is still present when CFN tries to delete the custom resource — for a healthy cluster, CFN's own `AWS::RDS::DBCluster` delete will already be in-flight and may have completed by then, and the Lambda's first check is "does the cluster still exist? If no → signal success and return."
- Not handling cross-region replicas. The Lambda is regional; each region's bootstrap deploys its own handler. Global cluster membership concerns are out of scope (the cluster's regional delete handles the per-region piece).

## Decisions

### D1. CFN custom resource (`Custom::AuroraClusterDrainOnDelete`) backed by a Lambda

The custom resource lives in `db.template` alongside the `AuroraCluster` resource it protects. It has one property: `ClusterIdentifier`. Its `ServiceToken` is the ARN of the `AuroraClusterDeleteHandler` Lambda, passed in via stack parameter (sourced from SSM by the workflow).

```yaml
AuroraClusterDrainOnDelete:
  Type: Custom::AuroraClusterDrainOnDelete
  DependsOn: AuroraCluster
  Properties:
    ServiceToken: !Ref AuroraClusterDeleteHandlerArn
    ClusterIdentifier: !Ref AuroraCluster
```

**Why `DependsOn: AuroraCluster`:** CFN deletion order is the reverse of creation order. Creating `AuroraCluster` first then `AuroraClusterDrainOnDelete` (via DependsOn) means that on delete, `AuroraClusterDrainOnDelete` is deleted *first*. The Lambda runs, drains the cluster, then CFN's own `AuroraCluster` delete (which runs next) finds the cluster already gone and reports success.

**Why a separate resource rather than overriding the `AuroraCluster` resource:** the `Custom::*` resource type can't replace a native `AWS::RDS::DBCluster` resource — they're different CFN resource types. The cleanest design is two cooperating resources: the native one for create/update, the custom one for guaranteed deletion.

**Alternative considered:** Use [`AWS::CloudFormation::CustomResource`](https://docs.aws.amazon.com/AWSCloudFormation/latest/UserGuide/aws-resource-cfn-customresource.html) directly as the resource type instead of `Custom::*`. Rejected — `Custom::*` allows a more meaningful logical type name in the template, which makes the intent clearer to readers.

### D2. Lambda lives in bootstrap (one per region)

The Lambda is shared infrastructure — all env stacks in a given region use the same handler. It belongs in `bootstrap.template` (the single regional shared-infrastructure stack we built in the parent change).

**Why bootstrap and not per-env:** per-env Lambda means each `dev`/`alpha`/`beta`/`app` stack carries its own copy with its own IAM role, log group, and update lifecycle. With dozens of feature-branch stacks, that's significant proliferation for no benefit — the Lambda's logic is identical across envs.

**Why not gated on `IsPrimary` (the way IAM users are):** Lambda functions are *regional*, not global. Each region's clusters need a Lambda *in that region* to handle the delete (cross-region Lambda invocations from a CFN custom resource don't work cleanly). So `bootstrap` creates one Lambda per region, both us-east-1 and us-west-2 (and any future region).

### D3. SSM-published Lambda ARN, threaded through the parameter chain

Discovery follows the same pattern as the KMS keys (already in production): bootstrap publishes the Lambda's ARN at a known SSM path, the workflow does an `aws ssm get-parameter` lookup at deploy time, and passes the value as a stack parameter through `master → backend → db`.

```
/${AWS::StackName}/lambda/aurora-cluster-delete-handler-arn   (in each region)
e.g. /appcloud-systems/lambda/aurora-cluster-delete-handler-arn
```

> **Updated during implementation:** the legacy draft used a hardcoded
> `/taskmanager/...` path. The `domain-derived-resource-naming` refactor has
> since made every SSM path derive from the dashed domain (`${AWS::StackName}`
> in bootstrap, `${processed-domain}` in the workflow), so this change follows
> that convention to match the existing KMS key parameters.

**Why not `Fn::ImportValue`:** same reasons as for the KMS ARN — Exports create rigid cross-stack coupling that prevents bootstrap updates while consumers exist. SSM is the established pattern in this codebase.

**Why pass through `master → backend → db` rather than letting `db.template` look up SSM directly:** CFN templates can use the `AWS::SSM::Parameter::Value<String>` parameter type to lookup at deploy time, which would let `db.template` resolve it directly without parameter threading. We could do this — but consistency with the KMS-arn parameter (which is workflow-resolved and threaded down) wins. Mixing lookup strategies in the same stack chain is confusing.

### D4. Lambda behavior on each event type

```
event.RequestType == "Create": Signal SUCCESS immediately. No-op.
event.RequestType == "Update": Signal SUCCESS immediately. No-op.
event.RequestType == "Delete":
   1. DescribeDBClusters(ClusterIdentifier). 
      If ClusterNotFoundFault → Signal SUCCESS. Already gone.
   2. If cluster.DeletionProtection == true:
        ModifyDBCluster(DeletionProtection=false, ApplyImmediately=true).
        Re-Describe to confirm.
   3. For each DBClusterMember in cluster.DBClusterMembers:
        DeleteDBInstance(member.DBInstanceIdentifier, SkipFinalSnapshot=true).
   4. Wait (polling, ~30s intervals, ~30 min timeout) for cluster to leave
      transient states: backing-up, creating, modifying, configuring-iam-database-auth.
      If cluster reaches available, stopped, or any deletable state → proceed.
      If timeout → Signal FAILED with the last observed cluster status.
   5. DeleteDBCluster(ClusterIdentifier, SkipFinalSnapshot=true).
   6. Wait for cluster to be ClusterNotFoundFault.
   7. Signal SUCCESS.

Any unexpected exception → Signal FAILED with the exception message
(so CFN's DELETE_FAILED carries the actual root cause, not a downstream symptom).
```

The wait-for-stable-then-delete step (#4) is the critical piece. A cluster stuck `backing-up` indefinitely (the original failure mode) will eventually time out and signal FAILED with a clear "cluster stuck in backing-up for 30 min" message — far more useful than CFN's generic "subnet group is still using it" downstream error.

CFN signaling uses the `cfn-response` module (Lambda runtime: `python3.12`) — a well-trodden pattern.

### D5. Lambda IAM permissions: scoped to this deployment's clusters only

The Lambda's IAM role policy:

```yaml
Statement:
  - Effect: Allow
    Action:
      - rds:DescribeDBClusters
      - rds:DescribeDBInstances
    Resource: "*"   # Describe APIs don't support resource-level perms

  - Effect: Allow
    Action:
      - rds:DeleteDBCluster
      - rds:ModifyDBCluster
    Resource: !Sub arn:aws:rds:${AWS::Region}:${AWS::AccountId}:cluster:*

  - Effect: Allow
    Action:
      - rds:DeleteDBInstance
    Resource: !Sub arn:aws:rds:${AWS::Region}:${AWS::AccountId}:db:*

  - Effect: Allow
    Action:
      - kms:DescribeKey
    Resource: "*"   # for cluster KMS sanity checks in logs

  - Effect: Allow
    Action:
      - logs:CreateLogGroup
      - logs:CreateLogStream
      - logs:PutLogEvents
    Resource: !Sub arn:aws:logs:${AWS::Region}:${AWS::AccountId}:*
```

**Note on the resource scoping (revised twice during implementation):** the legacy
draft assumed clusters were named `taskmanager-*`. They are not — under the current
convention `db.template` lets CloudFormation auto-name the cluster (e.g.
`dev-appcloud-systems-backendstack-7d-auroracluster-<rand>`), which has no stable
prefix to scope against. An interim revision tried to restore a tight constraint by
giving the cluster an **explicit** `DBClusterIdentifier` of `${DomainDashed}-${BranchName}`
and scoping to `${AWS::StackName}-*`. **That was reversed** (see tasks §0.1): an
explicit identifier forces a cluster **replacement**, and the replacement's new
endpoint changes the `DatabaseHost` export — which CloudFormation refuses to update
while the env's Web/Api stacks **and every feature branch's app stack** import it
(`application.template` imports backend exports from `dev`). The dev deploy on
2026-06-05 rolled back on exactly this error.

**Final decision — no rename, region-scoped grant:** keep the cluster/instance
auto-named and scope the policy to `cluster:*`/`db:*` within the deploying region.
Auto-names can't be prefix-matched from the region-wide bootstrap role, and a
safety-net teardown that silently lacks delete permission is worse than a broad
in-region grant. The Lambda only ever deletes the one cluster the custom resource
names in its `Delete` event, so the practical blast radius is unchanged. This also
removes all replacement/data-loss risk from the rollout (tasks §5). **Future
hardening:** tag env clusters with the domain and constrain via `aws:ResourceTag`.

### D6. Custom-resource property change handling

The custom resource has `ClusterIdentifier` as its only property. If a stack update changes `ClusterIdentifier` (e.g., the cluster gets renamed somehow), CFN's default custom-resource behavior is to call the Lambda with `RequestType=Update` and the new properties. Our Lambda treats Update as a no-op, so renames are tolerated without misbehavior.

If a stack update *removes* the custom resource entirely (e.g., someone refactors the template), CFN calls `RequestType=Delete` on the old resource. The Lambda would try to drain the cluster — which may not be desired in an update context. We accept this as a "don't refactor the custom resource away while clusters exist" operational constraint, documented in the template comment.

## Risks / Trade-offs

- **[Risk] Lambda timeout (default 3s) is way too short.** → Mitigate: set Lambda `Timeout: 900` (15 min, max for Lambda). Combined with the polling waiter, the handler can wait for clusters that take longer than expected to delete. For clusters that stay stuck >15 min, the Lambda times out and CFN gets a generic timeout error — operator should investigate manually. We document this in the Lambda comments.
- **[Risk] Lambda execution role drift.** → Mitigate: role is created in the same stack as the Lambda (bootstrap). Inline policies, not attached managed policies, so a console operator can't easily modify them without CFN drift detecting.
- **[Risk] What if `ApplyImmediately` on `ModifyDBCluster` fails because the cluster is in a non-modifiable state?** → The Lambda catches that exception and proceeds anyway to step 4 (wait-for-stable). If the cluster never stabilizes within the timeout, the handler signals FAILED with a clear message. Worst case is what we have today; best case is fast recovery.
- **[Risk] Lambda + custom resource adds resources to every env stack and every bootstrap stack — increased blast radius.** → Mitigate: Lambda code is short (~80 lines), well-tested patterns; the custom resource is a no-op for CREATE/UPDATE so it won't affect the success path. Risk is bounded.
- **[Risk] First post-change deploy adds the custom resource to existing env stacks. If the CREATE of the custom resource fails for any reason (e.g., Lambda not yet deployed in this region), the env-stack update fails.** → Mitigate: deploy bootstrap (with Lambda) to all regions FIRST, then push the workflow/template change. Tasks.md sequences this explicitly.
- **[Trade-off] Custom resource execution adds ~30 seconds to every env-stack delete (Lambda cold start + at minimum one DescribeDBClusters call).** → Acceptable. Stack deletes are infrequent operations.
- **[Trade-off] We're adding a Lambda the project doesn't currently use. Operationally one more thing to monitor (log group, dead-letter, etc.).** → Mitigate: log group has same retention policy as other bootstrap resources; failures are surfaced through CFN itself rather than requiring separate alerting.

## Migration Plan

**Phase 0 — Prereqs**
1. Confirm the `centralize-aurora-kms-keys` change is fully landed (this change builds on the consolidated `bootstrap` stack pattern).

**Phase 1 — Lambda + bootstrap update**
2. Edit `bootstrap.template` to add the `AuroraClusterDeleteHandler` Lambda + IAM role + log group + SSM parameter publishing the Lambda ARN. Both primary and replica region deploys create the Lambda (it's regional, not global).
3. Run `aws cloudformation validate-template`.
4. Operator (human admin) deploys the updated `bootstrap` stack to `us-east-1` and `us-west-2`. Verify the new SSM parameter resolves.

**Phase 2 — Template + workflow wiring**
5. Edit `master.template`, `backend.template`, `db.template` to add the `AuroraClusterDeleteHandlerArn` parameter, threading it down to the custom resource in `db.template`.
6. Edit `zbuild.yml` to add the SSM lookup step and pass the resulting value as a parameter override.
7. Run `aws cloudformation validate-template` on the four touched templates.

**Phase 3 — Roll out to env stacks**
8. Push to a throwaway feature branch first — verifies the workflow's new SSM-lookup step works. Feature branches deploy `application.template` (no Aurora cluster), so the custom resource isn't created — but the workflow plumbing is exercised.
9. Push to `dev` (or merge into `dev`) — triggers env-stack update which adds the custom resource to dev's existing stack. CREATE handler is no-op, so the deploy should succeed cleanly.
10. Trigger a dev stack delete to test the new path end-to-end: `aws cloudformation delete-stack --stack-name dev-appcloud-systems --region us-east-1`. Verify the cluster gets force-drained and the stack delete completes cleanly. Then re-trigger CI to recreate dev.
11. Push to `alpha`, `beta`, then `app` to roll out the custom resource to all env stacks.

**Phase 4 — Validation**
12. Inject a controlled failure to validate the recovery path: change one env stack's cluster KMS key out from under it (out-of-band, via `aws kms put-key-policy` adding a Deny temporarily), confirm the cluster gets stuck, then trigger stack delete and verify the handler unsticks it. Restore the original key policy after.

**Rollback strategies:**
- Phase 1: delete the new Lambda + IAM role from `bootstrap.template`; redeploy bootstrap. Existing env stacks that already reference the SSM param would fail their next update — so this is only safely-reversible *before* Phase 3.
- Phase 2: revert the template + workflow edits before they merge; bootstrap stays as-is (Lambda exists but unused, harmless).
- Phase 3+: each env stack update is reversible by a follow-up update that removes the custom resource. The Lambda itself can stay around indefinitely as latent infrastructure.

## Open Questions

1. **Lambda runtime — `python3.12` or `nodejs20.x`?** Project already uses `python3.12` for the log-retention Lambda in bootstrap. Sticking with `python3.12` for consistency unless there's a reason to differ.
2. **Should the Lambda log to a dedicated CloudWatch metric alarm if it ever fires its actual force-delete logic?** That would surface "operator noticed the deadlock and triggered cleanup" as visible signal. Out of scope for this change — log group is enough for now.
3. **Should we add the same pattern for AWS::RDS::GlobalCluster (which has its own delete deadlocks for multi-region setups)?** Deferred. Currently only `app`/`beta`/`alpha` use Global Cluster; the GlobalCluster delete failure modes are different and warrant their own analysis.
