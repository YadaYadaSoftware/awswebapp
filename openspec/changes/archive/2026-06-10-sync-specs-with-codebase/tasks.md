## 1. Setup

- [x] 1.1 From `dev` (pulled current), create and check out branch `sync-specs-with-codebase`
- [x] 1.2 Confirm the working tree reflects `dev`'s state and is clean

## 2. Build the in-scope file list

- [x] 2.1 Enumerate every `.md`, `.yaml`, and `.yml` file in the repository
- [x] 2.2 Exclude every file whose path contains an `archive` directory segment
  (case-insensitive; e.g. `openspec/changes/archive/**`), recording each as `skipped-archived`
- [x] 2.3 Verify the exclusion does not drop files that merely contain "archive" in a filename
  (e.g. `archive-policy.md` stays in scope)
- [x] 2.4 Record the resulting in-scope list as the audit worklist (seeds the audit record)

## 3. Examine and reconcile each in-scope file

- [x] 3.1 For each in-scope OpenSpec spec under `openspec/specs/**`, compare requirements/scenarios
  against the code's actual behavior; update stale specs to match the code
- [x] 3.2 Audit top-level prose docs (`README.md`, `CLAUDE.md`, `BROTHERS.md`,
  `BRANCH_MANAGEMENT_README.md`) against the code; correct drift (e.g. README stack staleness)
- [x] 3.3 Audit `.yaml`/`.yml` files (e.g. `.github/workflows/**`, `openspec/config.yaml`) for
  documentary/claim-bearing drift; correct comments/descriptions that misstate behavior
- [x] 3.4 For each file, assign a verdict: `in-sync`, `updated`, `flagged-as-possible-bug`, or
  `skipped-archived`
- [x] 3.5 Leave `in-sync` files unmodified
- [x] 3.6 When code appears to contradict deliberate, still-intended behavior, record
  `flagged-as-possible-bug` instead of editing the doc to match the suspected defect

## 4. Capture out-of-scope follow-ups

- [x] 4.1 Collect every discrepancy that would require an app/CI/deploy behavior change
  (per the `CLAUDE.md` code-change gate) and note it for a separate OpenSpec change or bug fix —
  do not make those changes under this audit
- [x] 4.2 List all `flagged-as-possible-bug` items with symptom (what's wrong vs. expected)

## 5. Finalize the audit record

- [x] 5.1 Produce the auditable record: every examined file with its verdict, resumable if paused
- [x] 5.2 Re-scan to confirm no in-scope file was missed and no `archive` file was touched
- [x] 5.3 Summarize: counts per verdict, list of updated files, and follow-ups handed off
