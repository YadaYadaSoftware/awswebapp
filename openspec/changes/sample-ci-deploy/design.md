## Context

This change is the convergence point of two upstream tracks: wilhelm's `make-deployment-stack-reusable` (which parameterizes the CloudFormation templates and extracts the deploy job into a reusable `workflow_call` workflow + a `YadaYada.AwsWebApp.DeploymentStack` NuGet package) and the `extract-web-framework-package` → `sample-solution-local` track (which produces a NuGet-consuming sample app). Here the sample becomes a *deployed* second web application on the same machinery.

It is intentionally thin: if both upstreams did their jobs, this change is mostly wiring a caller workflow with the sample's inputs and writing deployed-URL UI tests. Anywhere it can't be thin, that's a signal of a gap in `make-deployment-stack-reusable`'s input surface — to be reported upstream, not patched here.

## Goals / Non-Goals

**Goals:**
- The sample deploys to `dev`/`alpha`/`beta`/`app` via the reusable workflow, multi-region where the model dictates, to its own subdomain and stacks.
- The sample's CI builds/tests `Sample.sln` on the published framework packages.
- UI tests run against the deployed sample URL with the same reporting as `Tjb.UiTests`.
- Tagging and branch-delete cleanup come for free from the reusable stack.
- Prove the end goal: more than one multi-regional web app from one framework.

**Non-Goals:**
- Building or modifying the reusable deployment stack (wilhelm's change).
- New infrastructure capabilities. The sample uses the stack as-is.
- The slipstream demonstration (`framework-slipstream-upgrade`).

## Decisions

### D1. The sample's CI is a thin caller of the reusable workflow

A workflow (in this repo, scoped to the sample's paths/branches, or a dedicated job) runs the sample's `.NET` build + test, then `uses:` the reusable `deploy.yml` with the sample's inputs and secrets. The reusable workflow extracts templates from `YadaYada.AwsWebApp.DeploymentStack` and runs the matrixed multi-region deploy. The sample provides: `project-name`, `domain-name`, `hosted-zone-id`, `web-project-path=src/sample/Sample.Web`, the Dockerfile path, `secondary-region`, `multi-region-branches`, `prod-branch`, and the `ui-test-filter` for `Sample.UiTests`.

**Open dependency:** the exact input names/shape are owned by `make-deployment-stack-reusable` (its design D3). This change consumes whatever that change finalizes; tasks include a step to reconcile against the actual published input surface.

### D2. Stack-coexistence model — same domain, distinct `ProjectName`, distinct leaf

The reusable stack scopes resource names by `ProjectName` and stack names by `{branch-leaf}-{processed-domain}`. The cleanest coexistence with TaskManager in the same account/domain is: the sample uses a **distinct `ProjectName`** (e.g. `sample`) and **distinct branch leaves**, so its KMS aliases/SSM paths/IAM scopes/Aurora identifiers and its CloudFormation stack names never collide with TaskManager's. Two questions go to design closure: (a) does the sample share TaskManager's domain (subdomain `sample.<domain>`) or get its own domain/hosted zone; (b) per the reusable stack's account model (its Open Question 1 — one consumer per account), whether the sample needs its own AWS account + bootstrap. Default assumption pending wilhelm's resolution: **own subdomain on the same domain**, **distinct `ProjectName`**, and a **sample-specific bootstrap stack** if the account model requires per-project bootstrap.

**Why distinct `ProjectName`, not shared:** the whole reusability premise is that `ProjectName` namespaces resources; using it is the intended path and the truest test of the reusable stack. Reusing TaskManager's `ProjectName` would defeat the demonstration.

### D3. Branch-delete cleanup and tagging are inherited, not reimplemented

Both come from the reusable stack: the six stack-level tags (computed in the reusable workflow's tag step) and the branch-delete teardown (the cleanup workflow computes the same `{branch-leaf}-{processed-domain}` name). The sample needs no tagging/cleanup code — only verification that its stacks are tagged and that deleting a sample feature branch tears down only its own stack. **GitHub Actions dormancy caveat:** `delete`/`schedule`-triggered workflows run from the *default branch* (`app`) copy; any cleanup behavior the sample relies on must already be promoted to `app`. Verify before relying on cleanup for sample feature branches.

### D4. Sample UI tests mirror `Tjb.UiTests`

`Sample.UiTests` (Playwright/xUnit) targets `https://<sample-leaf>.{DOMAIN_NAME}`, uses the token-based Google auth approach CI already uses for `Tjb.UiTests`, and emits a pinned TRX (`sample-ui-tests.trx`) consumed by `dorny/test-reporter` with uploaded `trace.zip` artifacts. The deploy gates on these as TaskManager's pipeline does.

## Risks / Trade-offs

- **[Risk — hard dependency] `make-deployment-stack-reusable` is not yet landed; its input surface is still TBD.** → This change cannot complete until it lands. Tasks front-load a reconciliation step against the actual reusable workflow. Sequence: do not start the caller-workflow tasks until the reusable `deploy.yml` is consumable.
- **[Risk] Per-account singletons** (`AWS::ApiGateway::Account`, templates-bucket policy) clash if the sample shares TaskManager's account. → Follow the account model `make-deployment-stack-reusable` documents (likely one project per account); provision the sample's bootstrap accordingly. Report any singleton clash upstream.
- **[Risk] SES per-region identity + sandbox** for the sample's domain if it sends confirmation emails. → Either reuse a verified identity or run the documented per-region SES setup for the sample's domain before relying on email; the sample can disable the post-registration email path if not needed for the deploy proof.
- **[Risk] Cost** — a second multi-regional Aurora Global Cluster + Fargate footprint on `app`/`beta`/`alpha`. → Flag to the user before deploying the sample to the shared-infrastructure branches; feature-branch single-region deploys are the cheap validation path and should come first.
- **[Trade-off] Two deployable apps in one repo** increase CI surface and concurrency. → The existing per-branch concurrency model already scopes runs by ref; sample branches are independent leaves.

## Open Questions

1. **Sample domain/subdomain and hosted zone** — `sample.<existing-domain>` vs a dedicated domain. Drives DNS/ACM and `hosted-zone-id`. Resolve with the user (cost + DNS ownership).
2. **AWS account model for the sample** — same account as TaskManager with `ProjectName` isolation, or its own account. Inherit `make-deployment-stack-reusable`'s decision (its Open Question 1).
3. **Does the sample deploy to the shared-infrastructure branches at all, or only feature branches for the demonstration?** Feature-branch deploy is enough to prove reusability cheaply; `app`/`beta`/`alpha` deploys cost real money. Recommend gating shared-infra deploys behind explicit user approval.
4. **Reconcile against the final reusable input surface** once `make-deployment-stack-reusable` lands — names may differ from its proposal's working list.
