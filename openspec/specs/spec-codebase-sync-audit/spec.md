# spec-codebase-sync-audit Specification

## Purpose
TBD - created by archiving change sync-specs-with-codebase. Update Purpose after archive.
## Requirements
### Requirement: Audit runs against the dev branch

The audit SHALL be performed against the state of the `dev` branch, on an implementing branch
named `sync-specs-with-codebase` cut from `dev`.

#### Scenario: Audit is initiated

- **WHEN** the spec-codebase synchronization audit is started
- **THEN** the working tree reflects `dev`'s current state (via a `sync-specs-with-codebase`
  branch cut from `dev`)
- **AND** the comparison baseline for "the codebase" is the source on that branch

### Requirement: File selection scope

The audit SHALL examine every file in the repository with a `.md`, `.yaml`, or `.yml` extension,
and SHALL examine no other file types.

#### Scenario: Markdown and YAML files are in scope

- **WHEN** the audit enumerates candidate files
- **THEN** every `.md`, `.yaml`, and `.yml` file in the repository is selected for examination
- **AND** files with any other extension (e.g. `.cs`, `.json`, `.template`) are not selected as
  audit targets (though their contents may be read as evidence of code behavior)

### Requirement: Archive folders are excluded

The audit MUST NOT read or modify any file whose path contains a directory segment named
`archive` (case-insensitive), including all archived OpenSpec changes under
`openspec/changes/archive/`.

#### Scenario: An archived spec is encountered

- **WHEN** file selection encounters a file under a path containing an `archive` segment
  (e.g. `openspec/changes/archive/<name>/specs/<cap>/spec.md`)
- **THEN** the file is excluded before it is opened
- **AND** the file is neither examined for drift nor modified
- **AND** it is recorded in the audit output with verdict `skipped-archived`

#### Scenario: A non-archive file with a similar name

- **WHEN** a file's path contains the word "archive" only inside a filename and not as a
  directory segment (e.g. `docs/archive-policy.md`)
- **THEN** the file is NOT excluded and is examined normally

### Requirement: Reconciliation against the codebase

For each in-scope file, the audit SHALL compare its claims about the system against observed code
behavior and reconcile any drift, treating the code as the source of truth for drift correction.

#### Scenario: A spec or doc contradicts the code (drift)

- **WHEN** an in-scope spec or doc states behavior that contradicts what the code actually does
  and the doc appears stale
- **THEN** the spec/doc is updated to accurately describe the code's actual behavior
- **AND** the file is recorded with verdict `updated`

#### Scenario: A file already matches the code

- **WHEN** an in-scope file's claims are consistent with observed code behavior
- **THEN** the file is left unmodified
- **AND** the file is recorded with verdict `in-sync`

#### Scenario: The code appears to contradict deliberate, still-intended behavior

- **WHEN** the code contradicts a spec/doc that describes behavior that still appears to be the
  deliberate intent (i.e. the code looks wrong rather than the doc being stale)
- **THEN** the discrepancy is recorded with verdict `flagged-as-possible-bug` rather than editing
  the doc to match the code
- **AND** the spec/doc is not silently changed to encode the suspected defect

### Requirement: Behavior changes stay out of scope

The audit SHALL NOT change application runtime behavior, EF migrations, or deploy-pipeline
behavior; documentation drift in `.yaml`/`.yml` files is corrected only in their human-readable,
claim-bearing portions.

#### Scenario: A discrepancy would require a code or pipeline behavior change

- **WHEN** reconciling a discrepancy would require changing running-app or build/deploy behavior
  (as defined by the `CLAUDE.md` code-change gate)
- **THEN** the audit does not make that change
- **AND** it records the need so it can be raised as a separate OpenSpec change or bug fix

### Requirement: Auditable per-file record

The audit SHALL produce a reviewable record listing every in-scope file together with its
verdict, so the pass is reviewable and resumable.

#### Scenario: The audit completes a pass

- **WHEN** the audit finishes (or is paused)
- **THEN** an audit record exists listing each examined file with one of the verdicts:
  `in-sync`, `updated`, `flagged-as-possible-bug`, or `skipped-archived`
- **AND** the record is sufficient to resume an interrupted pass without re-examining
  already-verdicted files

