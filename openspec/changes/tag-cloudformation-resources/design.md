## Context

Deploys flow through a single step in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml): the `Deploy template` step using `aws-actions/aws-cloudformation-github-deploy@v1`. Two template shapes go through it — `master.template` (nested `backend` + `application`) for `dev`/`app`/`beta`/`alpha`, and `application.template` for feature branches — but both are deployed by the same step against a stack named `${branch-leaf}-${processed-domain}`.

Today the templates declare essentially no tags (only two `HealthCheckTags` in `dns.template`). All the values we want as tags already exist as job outputs by the time the deploy step runs: branch leaf (`get-branch-name`), processed domain (`process-domain`), version (`build.customVersion`), and the GitHub run context. This makes the deploy step — not the templates — the natural single point of control.

## Goals / Non-Goals

**Goals:**
- Apply six tags (`Stack Name`, `Create Date`, `Branch`, `Specification`, `Version`, `DeployRunUrl`) to every taggable deployed resource.
- One mechanism that covers both deploy paths and any future resource without per-resource edits.
- Preserve a true `Create Date` across redeploys.
- Keep the pipeline domain/region/branch-agnostic.

**Non-Goals:**
- Tagging resources that CloudFormation does not auto-propagate stack tags to (documented under Risks).
- Backfilling tags onto resources outside this stack or onto already-deployed stacks except via their next normal deploy.
- Cost-allocation activation in the Billing console (a separate, account-level manual step once tags exist).

## Decisions

### Decision: Stack-level tags via the deploy action's `tags` input, not per-resource `Tags:` blocks

`aws-actions/aws-cloudformation-github-deploy@v1` accepts a `tags` input (a JSON array of `{Key,Value}`). CloudFormation applies those as **stack-level tags**, which it then propagates to every resource that supports tagging — including resources inside nested stacks (`master` → `backend`/`application` → children). One change covers ~11 templates and both deploy paths.

**Alternatives considered:**
- *Per-resource `Tags:` in every template* — verbose (dozens of edits across 11 files), easy to forget on new resources, and the values (branch, version, run url) would have to be threaded as parameters into every nested stack. Rejected for maintenance cost and drift risk.
- *`sam deploy --tags`* — the workflow uses `sam build`/`sam package` but deploys via the CFN GitHub action, not `sam deploy`; adding tags at the action is the smaller, consistent change.

### Decision: Compute tag values in a dedicated workflow step that emits a single JSON string

Add a step (after `process-domain` / `Set parameter overrides`, before `Deploy template`) that computes each value and emits a ready-to-use JSON array as a step output, consumed by the deploy step's `tags:` input. Keeping it one step keeps the Create Date lookback, the spec/sentinel rule, and JSON assembly together and testable.

JSON is assembled so that spaced keys (`Stack Name`, `Create Date`) are quoted exactly; values are injected as shell variables to avoid quoting hazards.

### Decision: Create Date via pre-deploy `describe-stacks` lookback

Before deploy:
```bash
EXISTING=$(aws cloudformation describe-stacks \
  --stack-name "$STACK_NAME" --region "$REGION" \
  --query "Stacks[0].Tags[?Key=='Create Date'].Value | [0]" \
  --output text 2>/dev/null || true)
if [ -n "$EXISTING" ] && [ "$EXISTING" != "None" ]; then
  CREATE_DATE="$EXISTING"
else
  CREATE_DATE=$(date -u +%F)
fi
```
`describe-stacks` errors when the stack does not exist (first deploy); the `2>/dev/null || true` and the empty/`None` checks make that path fall through to today's date without failing the job.

**Alternative:** always use `date -u` (last-deploy date). Rejected — the user wants a stable creation date; only `DeployRunUrl` is meant to change each deploy.

### Decision: Specification = branch leaf, sentinel `shared-infrastructure` for shared branches

Per CLAUDE.md, feature branches are named for their OpenSpec change, so the branch leaf already equals the spec name. The four shared-infrastructure branches (`app`/`beta`/`alpha`/`dev`) are not named for a single spec, so they get the fixed value `shared-infrastructure`. This reuses the same `app beta alpha dev` membership test the workflow already uses elsewhere.

### Decision: Tag value sources (summary)

| Tag | Source |
| --- | --- |
| `Stack Name` | `${branch-leaf}-${processed-domain}` (= the stack name) |
| `Create Date` | existing stack tag if present, else `date -u +%F` |
| `Branch` | `get-branch-name.outputs.branch-name` |
| `Specification` | branch leaf, or `shared-infrastructure` for `app`/`beta`/`alpha`/`dev` |
| `Version` | `build.outputs.customVersion` |
| `DeployRunUrl` | `${github.server_url}/${github.repository}/actions/runs/${github.run_id}` |

## Risks / Trade-offs

- **Stack tags do not reach every resource type.** CloudFormation propagates stack tags to most taggable resources, but some are not covered automatically — notably running **ECS tasks** (the service needs `PropagateTags: SERVICE`/`TASK_DEFINITION`) and ASG-launched EC2 instances. → The deployed surface is ECS Fargate behind an ALB; closing the ECS-task gap is a single `PropagateTags` property on the web service. Treat it as an in-scope optional task (decided during apply) and document any remaining gaps rather than chasing every edge resource.
- **`Create Date` lookback adds an AWS call before deploy.** → Cheap, read-only `describe-stacks`; guarded so a missing stack never fails the job.
- **Stale tags on existing resources until next deploy.** → Acceptable; tags are additive and converge on the next push. No backfill needed.
- **Cost allocation isn't automatic.** → Tags existing ≠ cost-allocation tags activated; activating them in the Billing console is a separate manual, account-level action noted for the operator, out of scope here.
- **Spaced/JSON quoting bugs could mangle keys.** → Build the JSON with the keys as literals and inject only values as variables; a post-deploy `describe-stacks` check in the validation task confirms the exact keys.

## Migration Plan

1. Land the workflow change on the `tag-cloudformation-resources` branch; its own deploy (stack `tag-cloudformation-resources-<dashed-domain>`) is the first live exercise of the tagging.
2. Inspect the resulting stack tags and a sample of nested resources (`describe-stacks`, `resourcegroupstaggingapi get-resources`).
3. Merge to `dev`; confirm the shared-branch sentinel and Create-Date preservation on the second `dev` deploy.
4. Roll outward to `app`/`beta`/`alpha` via the normal branch flow.

**Rollback:** remove the `tags:` input (and the compute step); existing stack tags persist harmlessly until the next deploy drops them. No data or availability impact.

## Open Questions

- Close the ECS-task propagation gap (`PropagateTags` on the web service) in this change, or defer to a follow-up? (Leaning: include it — it's one property.)
- Include the git SHA as part of `Version` (or a separate tag), given it's already passed as `DeploymentToken`? (Leaning: keep `Version` = SemVer only; SHA is recoverable via `DeployRunUrl`.)
