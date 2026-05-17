## 1. Pre-flight verification

> **Note:** Tasks 1.1–1.3 require AWS console / `gh` CLI access. Neither tool is installed in the implementation environment, so these are left unchecked for the human operator to confirm before the workflow's first real run. The workflow will fail loudly with AccessDenied if IAM is short.

- [ ] 1.1 Confirm the IAM principal behind `secrets.AWS_ACCESS_KEY_ID` already has `cloudformation:DeleteStack`, `cloudformation:DescribeStacks`, `cloudformation:DescribeStackEvents`, `s3:ListBucket`, and `s3:DeleteObject` on `arn:aws:s3:::cf-templates-{account}-us-east-1` / `.../*`. If not, extend the policy attached to that principal before merging the workflow.
- [ ] 1.2 Confirm `secrets.DOMAIN_NAME` exists at repo level (it is referenced by the deploy workflow; sanity-check the secret is still present).
- [ ] 1.3 Verify the GitHub Actions `delete` event is enabled for this repository (default yes — confirm no org-level workflow restriction strips it).

## 2. Workflow file

- [x] 2.1 Create `.github/workflows/cleanup-on-branch-delete.yml`.
- [x] 2.2 Set `on: delete` as the only trigger. Add a workflow-level `if: github.event.ref_type == 'branch'` to short-circuit tag deletions. *(Applied as a job-level `if`, which is equivalent for a single-job workflow and is the idiomatic GitHub Actions placement for event-shape filters.)*
- [x] 2.3 Add a single job `cleanup-stack` running on `ubuntu-latest` with `concurrency.group: deploy-us-east-1-${{ <branch-leaf expression> }}` and `cancel-in-progress: false`, matching the deploy workflow's concurrency group format.
- [x] 2.4 Step: compute `BRANCH_LEAF=${REF##*/}` and `PROCESSED_DOMAIN=$(echo "$DOMAIN_NAME" | tr '.' '-')` and export `STACK_NAME=$BRANCH_LEAF-$PROCESSED_DOMAIN` to `$GITHUB_ENV`. Echo all three for traceability.
- [x] 2.5 Step: protected-branch guard. If `BRANCH_LEAF` matches `app`, `beta`, `alpha`, or `dev`, write a protected-skip line to `$GITHUB_STEP_SUMMARY` and exit 0. Use the same `" app beta alpha dev "` string-contains pattern the deploy workflow uses for consistency.
- [x] 2.6 Step: `aws-actions/configure-aws-credentials@v4` pinned to `us-east-1`, reusing `secrets.AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`.
- [x] 2.7 Step: pre-check. `aws cloudformation describe-stacks --stack-name $STACK_NAME --region us-east-1` and set a step output `stack_exists=true|false`. Treat any non-existence error as `false`; surface any other error as a workflow failure.
- [x] 2.8 Step: `aws cloudformation delete-stack --stack-name $STACK_NAME --region us-east-1` (skip if `stack_exists=false`).
- [x] 2.9 Step: `aws cloudformation wait stack-delete-complete --stack-name $STACK_NAME --region us-east-1` (skip if `stack_exists=false`). Capture exit code; on failure, run `aws cloudformation describe-stack-events --stack-name $STACK_NAME --region us-east-1 --max-items 25` and dump the result to the step log + summary, then `exit 1`.
- [x] 2.10 Step: read the final `StackStatus` after the waiter returns (only meaningful on failure paths where the stack still exists) and write it to the summary.
- [x] 2.11 Step: S3 cleanup. Only run if the deletion path succeeded. Resolve the bucket name as `cf-templates-${ACCOUNT_ID}-us-east-1` (account ID from `aws sts get-caller-identity`). Use `aws s3 rm "s3://$BUCKET/$BRANCH_LEAF/" --recursive` and capture the count of deleted objects from its output. Treat an empty/missing prefix as success.
- [x] 2.12 Step: write the final workflow summary (deleted ref, branch-leaf, computed stack name, region, pre-check result, final stack status or `DELETE_COMPLETE`, S3 objects removed). Use `if: always()` so the summary is written even on failure or early skip.

## 3. Local syntax check

- [ ] 3.1 Run `actionlint` (or paste into GitHub's online YAML validator) on the new workflow to catch syntax errors before pushing. *(`actionlint` and `python`/`pyyaml` are not installed in this environment, so automated local validation could not run. Please run actionlint locally or rely on GitHub's push-time YAML validation.)*
- [x] 3.2 Eyeball the YAML against `.github/workflows/zbuild.yml` to confirm step style (multi-line `run:` blocks, env handling) is consistent with the rest of the repo. *(Reviewed during writing — uses the same `run: |` block style, `$GITHUB_ENV` / `$GITHUB_OUTPUT` echo pattern, and `aws-actions/configure-aws-credentials@v4` action as zbuild.yml.)*

## 4. Live verification (low-risk)

> **Note:** Tasks 4.1–4.3 are live AWS smoketests that require pushing the workflow first. Left unchecked for the human operator.

- [ ] 4.1 Create a throwaway feature branch (`scripts/create-branch.ps1 feature/cleanup-smoketest`), let the deploy workflow create its stack, and confirm `feature/cleanup-smoketest`'s stack `cleanup-smoketest-appcloud-systems` exists in `us-east-1` CloudFormation.
- [ ] 4.2 Delete the throwaway branch from GitHub. Watch the new workflow run.
- [ ] 4.3 Confirm the workflow succeeds, the stack reaches `DELETE_COMPLETE`, the S3 prefix `s3://cf-templates-{account}-us-east-1/cleanup-smoketest/` is empty, and the workflow summary contains all expected fields.

## 5. Negative-path verification

> **Note:** Tasks 5.1–5.2 are live AWS tests. Left unchecked for the human operator.

- [ ] 5.1 In a scratch run (manual `gh workflow run` or by temporarily relaxing the trigger), confirm that supplying a `ref` of `dev` causes the protected-branch step to skip and the workflow to exit green without making any AWS calls. (Or rely on the unit-of-behavior check in 5.2 if you'd rather not touch the trigger.)
- [ ] 5.2 Manually delete a branch that never deployed (no stack ever created). Confirm the pre-check reports `stack_exists=false` and the workflow exits green with a "nothing to delete" summary line.

## 6. Documentation

- [x] 6.1 Add a short paragraph to `BRANCH_MANAGEMENT_README.md` describing the auto-cleanup behavior (trigger, protected branches, what gets deleted, where the workflow lives).
- [x] 6.2 Note in `CLAUDE.md` "Things that will trip you up" that deleting a branch on the remote now triggers a CloudFormation stack delete in `us-east-1`, and that `app`/`beta`/`alpha`/`dev` are exempt.

## 7. Out-of-scope follow-ups (capture, do not implement)

> **Note:** Both follow-ups are tracking-only — they belong in an external issue tracker rather than as code changes in this repo. Left unchecked for the human operator to file (or to skip if not relevant).

- [ ] 7.1 File a tracking note for an ECR image-pruning policy (lifecycle rule on the shared ECR repo to expire untagged or branch-tagged images after N days). Not part of this change.
- [ ] 7.2 File a tracking note to scan for already-orphaned stacks predating this workflow (`aws cloudformation list-stacks --region us-east-1` filtered by `StackName` matching `-appcloud-systems` whose corresponding branch no longer exists on the remote).
