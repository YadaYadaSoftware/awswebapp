## 1. Compute tag values in the workflow

- [x] 1.1 Add a `Compute resource tags` step to the `deploy` job, gated on `matrix.deploy`, after `Set parameter overrides` and before `Deploy template`.
- [x] 1.2 Compute `STACK_NAME` as `${branch-leaf}-${processed-domain}` from job outputs (no hardcoded domain/branch/region).
- [x] 1.3 Compute `Create Date` via `describe-stacks` lookback (guarded `2>/dev/null || true`), reusing a non-empty/non-`None` value else `date -u +%F`.
- [x] 1.4 Compute `Specification`: branch leaf, sentinel `shared-infrastructure` for `app`/`beta`/`alpha`/`dev` (reusing the existing membership test).
- [x] 1.5 Set `Branch`=leaf, `Version`=`needs.build.outputs.customVersion`, `DeployRunUrl`=run URL.
- [x] 1.6 Assemble the `{Key,Value}` JSON array with keys as literals (spaces preserved) and only values injected (via `jq -n --arg`); emit as step output `tags-json`. — *Verified locally: `jq` produces the exact array with `Stack Name`/`Create Date` intact.*

## 2. Wire tags into the deploy

- [x] 2.1 Add `tags: ${{ steps.compute-tags.outputs.tags-json }}` to the `Deploy template` step.
- [x] 2.2 Both deploy paths inherit it (master + application) — same step, no per-path branching.

## 3. Close known propagation gap

- [x] 3.1 Set `PropagateTags: SERVICE` on the web ECS service ([infrastructure/web.template](../../../infrastructure/web.template)) so running tasks inherit the stack tags. — *Included (per the design's leaning); template `validate-template` passes.*
- [x] 3.2 Record remaining propagation limitations. — *ECS tasks are now covered. Other auto-propagation gaps (e.g. ASG-launched EC2) don't apply — the deployed surface is ECS Fargate behind an ALB. Any non-taggable/edge resource simply won't carry the tags; documented in design Risks.*

## 4. Validate

- [x] 4.1 Confirm no template/build regressions from the `PropagateTags` edit. — *Done via `aws cloudformation validate-template` on web.template (the correct check for a template edit; the `.NET` `dotnet build` is unaffected by CFN-template changes).*
- [ ] 4.2 Deploy the branch and `describe-stacks` — confirm all six tag keys present with exact spaced keys. — *Pending the deploy on this push (will verify live with AWS access).*
- [ ] 4.3 Spot-check propagation via `resourcegroupstaggingapi get-resources --tag-filters Key=Branch,Values=tag-cloudformation-resources`. — *Pending deploy.*
- [ ] 4.4 Redeploy and confirm `Create Date` unchanged while `DeployRunUrl`/`Version` update. — *Pending a second deploy.*
- [ ] 4.5 Confirm a `dev` deploy yields `Specification=shared-infrastructure`. — *Pending dev deploy (after merge).*

## 5. Document

- [x] 5.1 Note the tag set, value sources, and the separate Billing-console cost-allocation step in CLAUDE.md. — *Added a "Resource tagging" section.*
