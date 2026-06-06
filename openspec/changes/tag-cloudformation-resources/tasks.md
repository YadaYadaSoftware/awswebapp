## 1. Compute tag values in the workflow

- [ ] 1.1 Add a `Compute resource tags` step to the `deploy` job in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml), gated on `matrix.deploy`, placed after `Process Domain Name` / `Set parameter overrides` and before `Deploy template`.
- [ ] 1.2 Compute `STACK_NAME` as `${branch-leaf}-${processed-domain}` from existing job outputs (do not hardcode domain/branch/region).
- [ ] 1.3 Compute `Create Date`: read the existing stack tag via `aws cloudformation describe-stacks --stack-name "$STACK_NAME" --region "${{ matrix.region }}" --query "Stacks[0].Tags[?Key=='Create Date'].Value | [0]" --output text` guarded with `2>/dev/null || true`; reuse the value if non-empty and not `None`, else `date -u +%F`.
- [ ] 1.4 Compute `Specification`: branch leaf for non-shared branches, sentinel `shared-infrastructure` for `app`/`beta`/`alpha`/`dev` (reuse the existing membership test pattern).
- [ ] 1.5 Set `Branch` = branch leaf, `Version` = `needs.build.outputs.customVersion`, `DeployRunUrl` = `${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}`.
- [ ] 1.6 Assemble a JSON array of `{Key,Value}` objects with keys as literals (so `Stack Name`/`Create Date` keep their spaces) and only values injected; emit it as a step output (e.g. `tags-json`).

## 2. Wire tags into the deploy

- [ ] 2.1 Add `tags: ${{ steps.<compute-step-id>.outputs.tags-json }}` to the `Deploy template` step (`aws-actions/aws-cloudformation-github-deploy@v1`).
- [ ] 2.2 Confirm both deploy paths inherit it: master template (`dev`/`app`/`beta`/`alpha`) and application template (feature branches) — no per-path branching needed since both use the same step.

## 3. Close known propagation gap (optional, per design open question)

- [ ] 3.1 Decide whether to set `PropagateTags` on the web ECS service so running tasks inherit the stack tags; if yes, add the property in the relevant template ([infrastructure/web.template](../../../infrastructure/web.template)).
- [ ] 3.2 Record in the design/PR notes any resource types that still do not receive propagated tags (documented limitation).

## 4. Validate

- [ ] 4.1 Run `dotnet build --configuration Release` locally to confirm no template/build regressions from any template edits (if step 3 added a property).
- [ ] 4.2 Deploy the `tag-cloudformation-resources` branch and run `aws cloudformation describe-stacks --stack-name tag-cloudformation-resources-<dashed-domain>` — confirm all six tag keys present with correct values and exact spaced keys.
- [ ] 4.3 Spot-check propagation: `aws resourcegroupstaggingapi get-resources --tag-filters Key=Branch,Values=tag-cloudformation-resources` (or inspect a nested-stack resource) and confirm the six tags appear on a backend/application resource.
- [ ] 4.4 Redeploy and confirm `Create Date` is unchanged while `DeployRunUrl`/`Version` update.
- [ ] 4.5 Confirm a `dev` deploy yields `Specification=shared-infrastructure`.

## 5. Document

- [ ] 5.1 Note in CLAUDE.md (or a deploy/ops doc) the tag set, value sources, and that cost-allocation activation in the Billing console is a separate manual account-level step.
