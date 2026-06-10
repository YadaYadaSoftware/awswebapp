## Why

The OpenSpec specs under `openspec/specs/` and the prose documentation (`.md`) and CI/infra
definitions (`.yaml`) drift from the running codebase over time as branches merge into `dev`
without a systematic reconciliation pass. There is currently no defined, repeatable procedure
for auditing every spec/doc against what the code actually does and correcting the drift, so
stale requirements accumulate silently (the README PostgreSQL/Lambda staleness called out in
`CLAUDE.md` is a concrete symptom). This change defines that audit as an explicit, auditable
capability so it can be run deliberately against `dev`.

## What Changes

- Introduce a **spec/documentation synchronization audit** capability: a defined procedure for
  examining the codebase on `dev` and reconciling every out-of-sync spec, doc, and config file
  with observed code behavior.
- Define the audit's **scope**: every `.md` and `.yaml`/`.yml` file in the repository is in
  scope for examination, **except** any file located under an `archive` folder (e.g.
  `openspec/changes/archive/**`) — archived specs are historical records and MUST NOT be
  examined or modified.
- Define the **reconciliation rules**: when a spec/doc contradicts observed code behavior, the
  spec/doc is updated to match the code (code is the source of truth); discrepancies that look
  like code bugs (code contradicts a deliberate, still-intended spec) are reported rather than
  silently "fixed" in the docs.
- Define the **audit output**: a record of every file examined, its verdict (in-sync / updated /
  flagged-as-possible-bug / skipped-archived), so the pass is reviewable.

## Capabilities

### New Capabilities
- `spec-codebase-sync-audit`: a repeatable procedure, run against `dev`, for examining all `.md`
  and `.yaml` files (excluding any `archive` folder) and reconciling out-of-sync specs and docs
  with the actual codebase, with an auditable record of outcomes.

### Modified Capabilities
<!-- None. This change defines the audit procedure itself. The spec/doc content corrections it
     produces are applied during /opsx:apply and may touch many existing specs, but those edits
     are reconciliation outputs governed by this audit capability rather than requirement-level
     changes proposed here. -->

## Impact

- **Specs**: any spec under `openspec/specs/**` (excluding `openspec/changes/archive/**`) may be
  updated to match the code during application of this change.
- **Docs**: `README.md`, `CLAUDE.md`, `BROTHERS.md`, `BRANCH_MANAGEMENT_README.md`, and other
  `.md` files may be corrected.
- **Config/CI**: `.yaml`/`.yml` files (e.g. `.github/workflows/**`, `openspec/config.yaml`) are
  examined for documentation drift; CI workflow *behavior* changes remain gated by the normal
  OpenSpec/bug-fix rules in `CLAUDE.md` and are out of scope for this audit's doc-reconciliation.
- **Branch**: per `CLAUDE.md`, implementation happens on a branch named `sync-specs-with-codebase`
  cut from `dev`.
- No runtime application code, EF migrations, or deploy-pipeline behavior changes as a direct
  result of this audit.
