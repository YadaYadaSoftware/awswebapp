## Context

After `centralize-aurora-kms-keys` landed, the env-stack template chain looks like this:

```
master.template
  ├─ BackendStack (backend.template)
  │   ├─ SecurityStack (security.template)   ← only resource: SharedLambdaExecutionRole
  │   ├─ NetworkingStack (network.template)
  │   ├─ DbStack (db.template)
  │   └─ InfrastructureStack (infrastructure.template)
  └─ ApplicationStack (application.template)
      ├─ ApiStack (api.template)              ← Fn::ImportValue "SharedLambdaRoleArn-${EnvironmentToImport}"
      ├─ WebStack (web.template)              ← Fn::ImportValue "SharedLambdaRoleArn-${EnvironmentToImport}" (×2)
      └─ DnsStack (dns.template)
```

`SharedLambdaExecutionRole` is created in **every** env stack (`dev`, `alpha`, `beta`, `app`) plus reused via `Fn::ImportValue` by feature branches that consume `dev`'s backend (`EnvironmentToImport=dev`). The role's `AssumeRolePolicyDocument` allows both `lambda.amazonaws.com` and `ecs-tasks.amazonaws.com` to assume it, and its inline policies (SecretsManagerAccess, CloudWatchLogs, RDSAccess, KMSAccess, SSMParameterAccess, ECRAccess, SesAccess) are identical across every env. Three observations:

1. **No env-specific anything.** The role's policies refer to AWS resource patterns (`arn:aws:rds:*:*:cluster:taskmanager-*`, `taskmanager/database/regional/*`, etc.) that match resources across all envs. There's no per-env IAM scoping in the inline policies — a `dev` task could in principle assume this role and reach `app` resources, except by deployment convention it doesn't.
2. **Operational duplication.** Every new env stack creates one more identical `taskmanager-*-SharedLambdaExecutionRole-<random>` IAM role. Auditing or updating the policies means touching N stacks.
3. **Cross-stack export dependency.** `api.template` and `web.template` use `Fn::ImportValue: !Sub "SharedLambdaRoleArn-${EnvironmentToImport}"` to pick up the role ARN. For feature branches, `EnvironmentToImport=dev`, which means **every feature-branch stack depends on dev's security stack export**. We can't update dev's security.template's `Export.Name` (or delete the export) without first removing every consumer — a hard cross-stack coupling we'd rather not have.

The fix: replace the per-env duplication with a single bootstrap-owned IAM role, publish the ARN via SSM (the established discovery pattern in this codebase), and thread it down as a parameter rather than a cross-stack import.

## Goals / Non-Goals

**Goals:**

- One `SharedLambdaExecutionRole` per AWS account, owned by `bootstrap`, with a predictable name and a known ARN.
- Discovery via SSM (no `Fn::ImportValue`). Removes the cross-stack export coupling between feature branches and dev's security stack.
- `security.template` deleted; one less template file to maintain; backend.template loses a nested-stack hop.
- Zero behavioral change for the application. Same role, same policies, just owned by a different stack.

**Non-Goals:**

- **Not** changing the role's policies. The role's inline policies stay byte-identical to what `security.template` has today (so `simulate-principal-policy` results don't change). Tightening the policies (per-env scoping, etc.) is a separate exercise.
- **Not** splitting the role into per-purpose roles (one for Lambda, one for ECS tasks). The dual-service `AssumeRolePolicyDocument` is unusual but works; leaving it alone.
- **Not** moving the role into the env stack via a different template. The whole point is to lift it out.
- **Not** retaining the legacy `SharedLambdaRoleArn-${BranchName}` CloudFormation export. We're replacing it; any out-of-tree consumer (none expected, but) needs to migrate to SSM.

## Decisions

### D1. Bootstrap owns the role; explicit `RoleName` for predictability

```yaml
SharedLambdaExecutionRole:
  Type: AWS::IAM::Role
  Condition: IsPrimary           # IAM is global; only the primary-region bootstrap creates it
  Properties:
    RoleName: ${AWS::StackName}-shared-lambda-execution-role
    AssumeRolePolicyDocument:    # identical to security.template's
      ...
    Policies:                    # identical inline policies, lifted verbatim
      ...
```

The explicit `RoleName` matters for two reasons:
1. The replica-region bootstrap stack publishes the role ARN via `!Sub`, not `!GetAtt`. A predictable name lets that work without cross-region lookups.
2. Operators reading IAM in the AWS console can find it by name immediately, rather than scrolling through CFN-generated names like `bootstrap-appcloud-system-SharedLambdaExecutionRole-XXXX`.

**Alternative considered:** Let CFN auto-generate the name. Rejected for the two reasons above; the small cost (can't easily rename later without coordinated cross-region update) is acceptable.

### D2. SSM-published role ARN, both regions

Same pattern as the KMS keys and the Aurora-cluster-delete handler:

```
/${AWS::StackName}/iam/shared-lambda-role-arn   (in each region)
```

- Primary region: `Value: !GetAtt SharedLambdaExecutionRole.Arn`.
- Replica region: `Value: !Sub "arn:aws:iam::${AWS::AccountId}:role/${AWS::StackName}-shared-lambda-execution-role"`. The role doesn't exist in this stack's `Resources` block (`IsPrimary` excludes it), but its predictable ARN is known by construction.

Both region's SSM parameter resolves to the same global IAM role ARN. The point of having a per-region SSM parameter is regional locality for the workflow's `aws ssm get-parameter` call — env stacks deploying to us-west-2 read from `--region us-west-2`.

**Why not `Fn::ImportValue`:** the parent `centralize-aurora-kms-keys` change established this rationale (Exports create rigid coupling between bootstrap and every consumer; SSM doesn't). Same logic applies here.

### D3. Parameter threading vs. parameter-store lookup at template parse time

CFN has the `AWS::SSM::Parameter::Value<String>` parameter type that does an SSM lookup at deploy time, which would let `api.template` and `web.template` look up the role ARN directly without parameter threading. We don't use it here for two reasons:

1. **Consistency.** The KMS key ARN follows the workflow-resolved-then-threaded pattern. Mixing strategies in the same stack chain is confusing.
2. **Diagnostic visibility.** When the workflow logs `SharedLambdaRoleArn=arn:aws:iam::ACCT:role/...` in CI output, an operator can see exactly what got passed in. With `AWS::SSM::Parameter::Value`, the resolution happens silently inside CloudFormation.

Threading adds parameters to four templates (master, backend, application, api, web) but each addition is one line. Worth it.

### D4. Feature-branch path also needs the parameter (and the workflow's BASE_PARAMS too)

The current code has `api.template` and `web.template` use `Fn::ImportValue "SharedLambdaRoleArn-${EnvironmentToImport}"`. For feature branches, `EnvironmentToImport=dev`, so the import resolves to dev's role. After this change:

- Feature branches deploy `application.template` (not master.template). Today's workflow's "Set parameter overrides" step has a feature-branch path that just sets `EnvironmentToImport=dev`.
- After this change, that path needs to ALSO set `SharedLambdaRoleArn=${SHARED_LAMBDA_ROLE_ARN}`.

So the workflow's BASE_PARAMS construction needs `SharedLambdaRoleArn=...` added **outside** the master-only `if [[ " app beta alpha dev " == ... ]]` block — both paths need it.

The KMS-key parameter is master-only because feature branches' application.template doesn't take it. The Lambda role parameter is both because api.template / web.template (which deploy on both paths) need it.

### D5. Migration ordering: bootstrap update first, then template+workflow merge, then propagate via deploys

The danger ordering: if api.template stops importing the legacy export but the role isn't in bootstrap yet, every deploy breaks. So:

1. Update `bootstrap.template` to add the new role + SSM parameter. Deploy bootstrap. Now SSM has the ARN; nothing else changed.
2. Merge the template+workflow changes (rewrite api.template, web.template, application.template, backend.template, master.template, security.template-deletion, workflow's lookup step + BASE_PARAMS). All in one PR — they're a coherent unit.
3. CI on dev re-deploys the env stack with the new templates. The change replaces:
   - the `SecurityStack` nested stack (deleted)
   - `api.template`'s `TaskRole` reference (now `!Ref SharedLambdaRoleArn`)
   - `web.template`'s two `TaskRole` / `ExecutionRoleArn` references (same)
   - ECS task definitions get re-rendered → Fargate tasks get replaced (rolling deploy, brief).
4. Push alpha, beta, app — same migration applied to each.

**Rollback before step 2:** revert the bootstrap change; new role disappears, no consumer ever depended on it. Safe.
**Rollback after step 2 but before step 3 succeeds anywhere:** revert the merge; CI redeploys with the old templates that use `Fn::ImportValue`. The cross-stack import still works because the old `SecurityStack`'s `Export` hasn't been deleted yet (it dies with the SecurityStack on the next deploy). So this is reversible until step 3.
**After step 3 succeeds on dev:** the dev SecurityStack is gone, the legacy export is gone. Reverting now would require redeploying dev with the old security.template *and* the old export name. Mechanical but slow. After all four envs are migrated, this is "done."

## Risks / Trade-offs

- **[Risk] Concurrent env-stack deletes during the migration could race on `${AWS::StackName}-shared-lambda-execution-role` — but it's owned by bootstrap, not env stacks, so this shouldn't happen.** → Mitigate: the migration touches env-stack `Update`s only, not `Delete`s. If someone deletes an env stack mid-migration, the legacy per-env `SharedLambdaExecutionRole-XXXX` gets cleaned up with the env stack and the new bootstrap-owned role is untouched.
- **[Risk] The role name `${AWS::StackName}-shared-lambda-execution-role` collides with a pre-existing IAM role.** → Verify before deploy: `aws iam get-role --role-name ${AWS::StackName}-shared-lambda-execution-role`. Pre-flight task in tasks.md.
- **[Risk] ECS task replacement during the migration.** When `web.template`'s `TaskDefinition.TaskRoleArn` reference changes, CFN replaces the task definition, which forces an ECS service deployment, which rolls Fargate tasks. → Mitigate: this is the standard ECS rolling deployment story. Brief, not zero-downtime — but `dev`/`alpha`/`beta` don't have SLO, and `app` has the existing rolling-deploy story. Acceptable.
- **[Risk] One inline policy in the moved role references `taskmanager/database/regional/*` Secrets Manager paths — verify the existing prod secrets still match after the role moves.** → No actual path change; same role ARN concept but the role ARN itself changes (was `arn:aws:iam::ACCT:role/<env>-...-SharedLambdaExecutionRole-XXXX`, now `arn:aws:iam::ACCT:role/${AWS::StackName}-shared-lambda-execution-role`). Secrets-Manager resource policies (if any) referencing the old role ARN need to be checked. → Mitigate: tasks.md has an explicit check for any AWS resource (Secrets Manager, S3 buckets, KMS keys outside our control, etc.) whose own policies reference the *old* role ARN. We expect zero hits because we don't currently attach principal-specific policies to those resources, but verifying is cheap.
- **[Risk] CloudFormation may not delete `security.template` cleanly if any env stack's resources still reference the SecurityStack's outputs.** → Mitigate: backend.template no longer references `!GetAtt SecurityStack.Outputs.SharedLambdaRoleArn`; verify with a pre-merge `grep`. The `SecurityStack` resource itself is removed; on env-stack update, CFN deletes the nested SecurityStack, which deletes the role inside it. (For env stacks whose role was already exported and consumed by api.template/web.template, both consumers are also updated in the same merge to stop importing it before CFN tries to delete the export. Order: api/web nested-stack updates → SecurityStack delete. CFN should sequence this correctly.)
- **[Trade-off] We're betting that no consumer of `SharedLambdaRoleArn-${BranchName}` exists outside this repo's templates.** → Verified by `grep`. Tasks.md includes the grep step.
- **[Trade-off] One extra SSM lookup per CI deploy (~ms latency, negligible).** → Acceptable.

## Migration Plan

**Phase 0 — Prereqs**
1. Confirm `centralize-aurora-kms-keys` is fully archived (or at minimum all four env stacks are running the new templates).
2. Confirm no pre-existing IAM role named `${AWS::StackName}-shared-lambda-execution-role`: `aws iam get-role --role-name ${AWS::StackName}-shared-lambda-execution-role` should return `NoSuchEntity`.
3. Confirm no outside-this-repo consumer of `SharedLambdaRoleArn-${BranchName}` export: `aws cloudformation list-exports --query "Exports[?starts_with(Name, 'SharedLambdaRoleArn-')]"` shows only this-repo exports.

**Phase 1 — Bootstrap update**
4. Edit `bootstrap.template` to add `SharedLambdaExecutionRole` (Condition: IsPrimary, explicit RoleName, identical inline policies to security.template's) + `SharedLambdaRoleArnParameter` SSM resource + `SharedLambdaRoleArn` stack output.
5. Operator deploys updated bootstrap to us-east-1: `aws cloudformation deploy --stack-name bootstrap-appcloud-systems --template-file infrastructure/bootstrap.template --parameter-overrides TemplatesBucketName=... --capabilities CAPABILITY_NAMED_IAM --region us-east-1`.
6. Verify SSM parameter resolves: `aws ssm get-parameter --name /${AWS::StackName}/iam/shared-lambda-role-arn --region us-east-1`.
7. Same for us-west-2 with the replica region's `PrimaryNonprodKeyArn`/`PrimaryProdKeyArn`/`TemplatesBucketName` overrides.

**Phase 2 — Template + workflow merge**
8. Edit master.template + backend.template + application.template + api.template + web.template to add the `SharedLambdaRoleArn` parameter and replace `Fn::ImportValue` lookups with `!Ref`.
9. Edit zbuild.yml to add the SSM-lookup step and BASE_PARAMS append (in both branch paths).
10. Delete security.template.
11. `aws cloudformation validate-template` on all touched templates.
12. Open PR, review carefully (this is a 7-file change with subtle CFN delete-order semantics), merge.

**Phase 3 — Roll out via deploys**
13. CI on dev auto-fires after merge. Watch for:
    - `SecurityStack` deletion in dev's stack events.
    - Task definitions in ECS console showing the new TaskRoleArn.
    - Rolling Fargate task replacement on web service.
14. Push alpha → beta → app, in that order. Each is a master-template update that drops `SecurityStack` and rewires api/web to the bootstrap-owned role.
15. Push a couple of feature branches to verify the feature-branch path picks up the new role ARN via the workflow's BASE_PARAMS append (no master.template involved on the feature path).

**Phase 4 — Cleanup**
16. Drop the §9.1 follow-up task from `centralize-aurora-kms-keys/tasks.md` (this change is the implementation).
17. `openspec validate --strict` and archive this change.

**Rollback strategies:**
- Phase 1: revert the bootstrap change. New role + SSM disappear. No consumer ever depended on either. Safe.
- Phase 2: revert the merge. CI redeploys with the old templates, which still `Fn::ImportValue` the legacy export. Legacy SecurityStack outputs are still there because no env-stack update has run yet that would delete them.
- Phase 3: rollback per-env stack is mechanical (redeploy with the old templates that include `SecurityStack` and `Fn::ImportValue`). The per-env role gets recreated on the next deploy.
- After all envs migrated: rollback would mean reverting to per-env roles for every stack. Reversible but tedious; the change should be stable by then.

## Open Questions

1. **Should we keep a `SharedLambdaRoleArn` CloudFormation Export at the bootstrap level (alongside the SSM parameter) for parity with other bootstrap exports?** Probably no — the SSM pattern is what we standardized on for this kind of value; adding a redundant export reintroduces the cross-stack-export coupling we're trying to eliminate. Punt to "no" unless someone hits a concrete use case.
2. **Lambda runtime (a) attaching the role and (b) the role's policies — any drift introduced by the role being globally shared?** Lambda functions in different env stacks attach the same role today (it's the same policies, just different ARN). After the move, they attach the same role *and* the same ARN. No functional difference.
3. **Should `web.template`'s `ExecutionRoleArn` (used by Fargate to pull the container image / write logs) and `TaskRoleArn` (used by the running app at runtime) be the same role?** They are today (both reference the same import); leaving them the same after the change. Splitting them is a future hardening exercise.
