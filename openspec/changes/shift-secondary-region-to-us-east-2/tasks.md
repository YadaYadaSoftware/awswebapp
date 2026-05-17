## 1. Phase 0 — SES prereqs (manual)

- [ ] 1.1 Confirm the prior change [`centralize-aurora-kms-keys`](../centralize-aurora-kms-keys/proposal.md) is fully landed (all artifacts archived, all phases complete) before starting this change.
- [ ] 1.2 Run `aws sesv2 create-email-identity --email-identity appcloud.systems --region us-east-2`. Capture the three DKIM CNAMEs from the output.
- [ ] 1.3 Add the three DKIM CNAMEs to Route 53 hosted zone `Z06422172SASV44F5Y8VA`.
- [ ] 1.4 Verify `aws sesv2 get-email-identity --email-identity appcloud.systems --region us-east-2` shows `DkimAttributes.Status: SUCCESS` (may take 24h).
- [ ] 1.5 Submit SES production-access request for `us-east-2`: `aws sesv2 put-account-details --production-access-enabled --mail-type TRANSACTIONAL --website-url https://appcloud.systems --use-case-description "transactional confirmation emails for taskmanager" --additional-contact-email-addresses hounddog@gmail.com --contact-language EN --region us-east-2`. Track AWS turnaround (~24h).
- [ ] 1.6 In the meantime, while SES production-access is pending, verify `hounddog@gmail.com` for sandbox sending in `us-east-2`: `aws ses verify-email-identity --email-address hounddog@gmail.com --region us-east-2`. Confirm via the verification link emailed.

## 2. Phase 1 — Bootstrap stand-up in us-east-2 (manual, human admin)

- [ ] 2.1 The human admin from the prior change's Phase 0.1 deploys `bootstrap` to `us-east-2`: `aws cloudformation deploy --stack-name bootstrap --template-file infrastructure/bootstrap.template --parameter-overrides TemplatesBucketName=cf-templates-<account>-us-east-2 --capabilities CAPABILITY_NAMED_IAM --region us-east-2`. (No `BootstrapScope` parameter → unscoped stack.)
- [ ] 2.2 Same operator deploys `bootstrap-nonprod` to `us-east-2`: pass `BootstrapScope=nonprod IsPrimaryRegion=false PrimaryKeyArn=<bootstrap-nonprod us-east-1 key ARN>`.
- [ ] 2.3 Same operator deploys `bootstrap-prod` to `us-east-2`: pass `BootstrapScope=prod IsPrimaryRegion=false PrimaryKeyArn=<bootstrap-prod us-east-1 key ARN>`.
- [ ] 2.4 Verify SSM parameters in `us-east-2`: `aws ssm get-parameter --name /taskmanager/kms/nonprod/aurora-key-arn --region us-east-2` and `/taskmanager/kms/prod/aurora-key-arn`. Both should return ARNs of the new `AWS::KMS::ReplicaKey` resources in `us-east-2`.
- [ ] 2.5 Verify `bootstrap-prod` in `us-east-2` did NOT create a new `GitHubActionsUserProd` (IAM is global; the user was already created by the `us-east-1` stack). The `IsProdScope` IAM resources in bootstrap.template should be gated to only create in `us-east-1` — verify the gate works by inspecting stack resources.

## 3. Phase 2 — Workflow + docs update

- [ ] 3.1 Edit [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml): change `AWS_REGION_SECONDARY: us-west-2` → `us-east-2`. Change the matrix entry `- region: us-west-2` → `us-east-2`. Verify no `us-west-2` literal remains in the workflow file (`grep -n us-west-2 .github/workflows/zbuild.yml` returns nothing).
- [ ] 3.2 Verify [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml) is unaffected (it operates only in `us-east-1` per the existing spec). No change expected.
- [ ] 3.3 Edit [CLAUDE.md](../../../CLAUDE.md): replace `us-west-2` references with `us-east-2` in: "Three 'shared infrastructure' branches deploy multi-region (us-east-1 + ...)" and the SES section's currently-verified list and per-region setup language. Update the "Currently verified" note accordingly.
- [ ] 3.4 Edit [BRANCH_MANAGEMENT_README.md](../../../BRANCH_MANAGEMENT_README.md) — replace `us-west-2` references with `us-east-2`.
- [ ] 3.5 Open PR against `app`; review carefully; merge. After merge, default-branch-only event handlers (per `feedback_github_actions_default_branch_events`) will see the new region from the next event onward.

## 4. Phase 3 — Per-branch cutover (alpha → beta → app)

- [ ] 4.1 **`alpha` cutover.** Snapshot just-in-case: `aws rds create-db-cluster-snapshot --db-cluster-identifier <alpha usw2 cluster id> --db-cluster-snapshot-identifier pre-region-shift-alpha-uswest2-<YYYY-MM-DD> --region us-west-2`.
- [ ] 4.2 Get `alpha` Aurora `us-west-2` regional cluster ARN: `aws rds describe-db-clusters --region us-west-2 --query 'DBClusters[?starts_with(DBClusterIdentifier, `taskmanager-alpha`)].DBClusterArn'`.
- [ ] 4.3 Remove from global cluster: `aws rds remove-from-global-cluster --global-cluster-identifier taskmanager-alpha-global-cluster --db-cluster-identifier <arn-from-4.2>`. The `us-west-2` cluster becomes a standalone cluster (which we then delete in 4.4).
- [ ] 4.4 Delete `alpha`'s `us-west-2` env stack: `aws cloudformation delete-stack --stack-name alpha-appcloud-systems --region us-west-2`; wait for `DELETE_COMPLETE`.
- [ ] 4.5 Trigger CI on `alpha` (empty push or workflow re-run). Watch the deploy logs: the `us-east-2` matrix entry should now run, create the env stack, and the `AWS::RDS::DBCluster` resource should add itself to the global cluster (`GlobalClusterIdentifier: taskmanager-alpha-global-cluster`).
- [ ] 4.6 Verify global cluster membership: `aws rds describe-global-clusters --global-cluster-identifier taskmanager-alpha-global-cluster` shows two members in `us-east-1` (writer) and `us-east-2` (reader).
- [ ] 4.7 Smoke-test `alpha` via deployed URL (`https://alpha.appcloud.systems`).
- [ ] 4.8 **`beta` cutover.** Repeat 4.1–4.7 with `beta` substituted for `alpha`.
- [ ] 4.9 **`app` cutover.** Schedule a maintenance window with stakeholders. Notify ahead of time (the primary in `us-east-1` is unaffected, but the global-cluster reshape is the most production-touching step).
- [ ] 4.10 Repeat 4.1–4.7 with `app` substituted for `alpha`.
- [ ] 4.11 Confirm `app`'s `us-east-2` failover path works: in a controlled test, simulate `us-east-1` outage (e.g. scale ECS service to 0 briefly during the maintenance window); confirm Route 53 failover routes to `us-east-2`; restore.

## 5. Phase 4 — Cleanup

- [ ] 5.1 Delete `bootstrap-prod` in `us-west-2`: `aws cloudformation delete-stack --stack-name bootstrap-prod --region us-west-2`; wait for `DELETE_COMPLETE`. The `AWS::KMS::ReplicaKey` enters `PendingDeletion` with `PendingWindowInDays: 7`.
- [ ] 5.2 Delete `bootstrap-nonprod` in `us-west-2`: same.
- [ ] 5.3 Delete `bootstrap` in `us-west-2`: `aws cloudformation delete-stack --stack-name bootstrap --region us-west-2`; wait for `DELETE_COMPLETE`.
- [ ] 5.4 Verify no `us-west-2` CloudFormation stacks remain: `aws cloudformation list-stacks --region us-west-2 --stack-status-filter CREATE_COMPLETE UPDATE_COMPLETE ROLLBACK_COMPLETE --query 'StackSummaries[].StackName'` returns empty.
- [ ] 5.5 7 days after 5.1/5.2, verify the two KMS replica keys in `us-west-2` are now in `PendingDeletion` and will be physically removed at the end of the window.
- [ ] 5.6 Verify no `us-west-2` ECR images or S3 templates buckets remain billable: `aws ecr describe-repositories --region us-west-2` and `aws s3api list-buckets --query 'Buckets[?contains(Name, `us-west-2`)]'`. Delete any leftover `cf-templates-<account>-us-west-2` bucket contents if appropriate (verify the bucket itself is owned by us and not used elsewhere first).

## 6. Validation

- [ ] 6.1 Run `openspec validate shift-secondary-region-to-us-east-2 --strict` and resolve any issues.
- [ ] 6.2 Manually verify each `## Requirement` scenario from `specs/multi-region-deployment-topology/spec.md` against the deployed system:
  - Primary region for any branch is `us-east-1`.
  - Workflow env has `AWS_REGION_SECONDARY: us-east-2`; no `us-west-2` literal.
  - `alpha`/`beta`/`app` deploy two matrix entries; `dev` + feature branches deploy one.
  - Global cluster for `app` spans `us-east-1` + `us-east-2`.
  - SES domain identity verified in both regions.
  - Cleanup-on-branch-delete still targets `us-east-1` only.
  - Bootstrap stacks (all three scopes) exist in `us-east-2`; SSM parameters resolve.
  - No `us-west-2` stacks remain.
- [ ] 6.3 Run UI tests against `app` to confirm end-to-end health.
- [ ] 6.4 Archive this change per the experimental workflow (`/opsx:archive`).
