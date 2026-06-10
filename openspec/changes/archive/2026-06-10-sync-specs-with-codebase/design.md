## Context

OpenSpec specs (`openspec/specs/**`), prose docs (`*.md`), and CI/infra config (`*.yaml`/`*.yml`)
drift from the codebase as `{type}/{name}` branches merge into `dev` without a reconciliation
pass. `CLAUDE.md` already documents one drifted artifact (the root `README.md` describing
PostgreSQL/Lambda when the code uses MySQL/containers). This change defines a single, repeatable
audit performed on `dev` that walks the in-scope files, compares each against observed code
behavior, and corrects the drift — without reopening settled architecture decisions or editing
historical records.

The audit is cross-cutting (touches every spec and most docs), which is why it warrants a design
doc: the value is in the *procedure and guardrails*, not in any one edit.

## Goals / Non-Goals

**Goals:**
- A deterministic file-selection rule: examine every `.md`, `.yaml`, and `.yml` file in the repo.
- A hard exclusion: never read or modify any file whose path contains an `archive` segment
  (e.g. `openspec/changes/archive/**`).
- A reconciliation rule with a clear source of truth (the code) and a clear escalation path
  (flag suspected code bugs instead of papering over them in docs).
- An auditable per-file record so the pass is reviewable and resumable.

**Non-Goals:**
- Changing application runtime behavior, EF migrations, or deploy-pipeline behavior. Any such
  change discovered as necessary is raised as a separate OpenSpec change or bug fix per the
  `CLAUDE.md` code-change gate — it is not made under this audit.
- Rewriting or re-styling docs that are already accurate. Only drift is corrected.
- Auditing archived material or generating new feature specs.

## Decisions

**Decision: Code is the source of truth for reconciliation.**
When a spec/doc contradicts what the code actually does, update the spec/doc to match the code.
Rationale: these artifacts are descriptions of an existing, deployed system; the deployed
behavior is the ground truth users experience. Alternative considered — treating the spec as
authoritative and "fixing" the code — is wrong for a drift audit and would smuggle behavior
changes past the code-change gate.

**Decision: Suspected code bugs are flagged, not silently encoded into docs.**
If code contradicts a spec/doc that appears to describe *deliberate, still-intended* behavior
(i.e. the code looks wrong, not the doc), record it in the audit output as
`flagged-as-possible-bug` rather than editing the doc to match buggy code. Rationale: preserves
the distinction between drift (doc is stale) and defect (code is wrong); the latter follows the
bug-fix path.

**Decision: `archive` exclusion is path-segment based.**
Exclude any file whose path contains a directory segment named `archive` (case-insensitive),
covering `openspec/changes/archive/**` and any future archive folders. Rationale: archived
changes are immutable history; touching them corrupts the record. Implemented as a glob/path
filter applied before any file is opened, so excluded files are never even read.

**Decision: One audit-record artifact captures outcomes.**
Produce a single audit record (a checklist/table) listing every in-scope file with a verdict:
`in-sync`, `updated`, `flagged-as-possible-bug`, or `skipped-archived` (for visibility into what
the exclusion dropped). Rationale: makes the pass reviewable and lets a later run pick up where a
prior one stopped. This lives in the change's `tasks.md` checklist plus an audit-findings note.

**Decision: Run on `dev`, on a branch named `sync-specs-with-codebase`.**
Per `CLAUDE.md`, the implementing branch carries the spec name verbatim and is cut from `dev`,
so the audit reflects the integration branch's state.

## Risks / Trade-offs

- **Risk: Misclassifying a code bug as doc drift** (silently documenting wrong behavior).
  → Mitigation: the `flagged-as-possible-bug` verdict and the "code is truth only for *drift*"
  rule force an explicit judgment per discrepancy; when intent is unclear, flag rather than edit.
- **Risk: Scope creep into behavior changes** while "just updating docs."
  → Mitigation: explicit Non-Goal + reliance on the `CLAUDE.md` gate; any code/CI behavior edit
  spins out into its own change.
- **Risk: Large surface area** (every `.md`/`.yaml`) makes the pass long and easy to leave
  half-done. → Mitigation: per-file audit record makes progress explicit and the pass resumable.
- **Risk: Over-editing accurate docs.** → Mitigation: "only correct drift" rule; `in-sync`
  files are recorded but left untouched.

## Migration Plan

Not applicable — no runtime, schema, or deploy changes. Rollback is `git revert` of the
doc/spec edits. The audit record itself is additive (lives under the change folder).

## Open Questions

- Should `.yml` files that are pure CI mechanics (no human-readable claims to drift) be recorded
  as `in-sync` by default, or examined line-by-line? Current stance: examine, but only the
  documentary/claim-bearing portions are subject to correction; behavior stays gated.
