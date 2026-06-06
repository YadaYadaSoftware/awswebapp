## Why

AWS resources created by the deploy pipeline carry no consistent, attributable tags today (templates define essentially none). When several branch stacks, regions, and builds coexist, there is no reliable way to answer "which stack/branch/spec/build/deploy-run produced this resource?" from the AWS console, Cost Explorer, or a `resourcegroupstaggingapi` query. A uniform tag set makes every resource traceable for cost allocation, cleanup, and incident triage.

## What Changes

- Introduce a fixed set of CloudFormation **stack-level tags** applied at deploy time, which CloudFormation auto-propagates to every taggable resource and down into the nested stacks (`master` → `backend` + `application` → children). No per-resource `Tags:` blocks are added.
- Tag set: **Stack Name**, **Create Date**, **Branch**, **Specification**, **Version**, **DeployRunUrl**.
- The deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) computes these values from data it already has (branch leaf, processed domain, SemVer, GitHub run context) and passes them to the `aws-actions/aws-cloudformation-github-deploy` step via its `tags` input.
- **Create Date** is preserved as a true creation date: the workflow reads the existing stack's tag before deploy and only stamps a fresh date on first create.
- **Specification** resolves to the branch leaf (which equals the OpenSpec change name for feature branches) and falls back to the sentinel `shared-infrastructure` on the `app`/`beta`/`alpha`/`dev` branches.
- Document the known propagation gap (resources that do not inherit stack tags, e.g. ECS tasks unless the service sets `PropagateTags`) as a limitation, with a decision on whether closing it is in scope.

No breaking changes — tags are additive metadata; existing resources gain tags on their next deploy.

## Capabilities

### New Capabilities
- `resource-tagging`: Defines the canonical tag set applied to deployed AWS resources, the value-fulfillment rule for each tag, and the stack-level-propagation mechanism by which they reach every taggable resource.

### Modified Capabilities
<!-- None — no existing spec's requirements change. The deploy workflow is touched as implementation, but no current capability spec governs resource tagging. -->

## Impact

- **Workflow**: [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) — new/extended step to compute tag values (including the `describe-stacks` lookback for Create Date and the spec/sentinel rule) and a `tags:` input on the "Deploy template" step. Affects both the master-template path (`dev`/`app`/`beta`/`alpha`) and the application-template path (feature branches), since both flow through the same deploy step.
- **Templates**: no source changes required by the chosen mechanism; optionally one `PropagateTags` property on the ECS web service if the propagation gap is closed in scope.
- **Constraints**: must stay domain- and region-agnostic (derive values, never hardcode domain/branch/region per CLAUDE.md). Tag keys containing spaces (`Stack Name`, `Create Date`) require correct JSON quoting in the workflow.
