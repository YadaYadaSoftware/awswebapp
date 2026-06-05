---
name: "Next"
description: Tell me the next step on my spec, or suggest an unclaimed OpenSpec change no brother is on
category: Worktrees
tags: [worktree, brothers, openspec, next]
---

Work out **what I should do next** as this brother (work folder), and keep the family from doubling up on the same spec. See [BROTHERS.md](../../BROTHERS.md) for the family convention and [CLAUDE.md](../../CLAUDE.md) for the branch/OpenSpec rules.

> **Shell:** this is a Windows/PowerShell environment. Run the helper through the **PowerShell tool**, not Bash.

**Steps**

1. **Run the helper:**
   ```powershell
   powershell -NoProfile -File scripts\Get-NextStep.ps1
   ```
   It prints `NEXT-MODE: on-spec` or `NEXT-MODE: idle` and the data for that mode (read-only). Interpret it as below.

2. **If `NEXT-MODE: on-spec`** — I'm implementing the change named by `CHANGE`. Use `PROGRESS`, `NEXT-SECTION`, and `NEXT-TASK` to name the *concrete* next step, classifying it from the section/task wording:
   - The next task reads like writing code/templates (add, wire, implement, refactor) → next step is **letting me implement it**; offer `/opsx:apply` to work the task and tick the checkbox.
   - It reads like **testing/validation/verification** (test, validate, verify, `dotnet test`, `aws ... validate-template`) → next step is **running that test/validation** — say what to run.
   - It reads like a **manual/deploy/operator** action (deploy, run in console, SSM, push) → call that out as a step *you* (the human) take, not me.
   - `PROGRESS` shows all tasks complete → next step is final validation, then archive with `/opsx:archive`.
   Mention `WORKING-TREE` if there are uncommitted changes (commit/flush before switching). Answer in one or two sentences naming the change, where it stands (N/M), and the single next action.

3. **If `NEXT-MODE: idle`** — I'm not on a change. The helper lists every active change with its task progress, which brother (if any) has it (`CLAIMED-BY`), and a `STATE`, plus an `AVAILABLE` list of changes no brother is on and not yet done. **Suggest one** unclaimed change to pick up, using judgment:
   - Prefer a fresh change (`0/N`) for a clean start; flag a nearly-finished one (e.g. `18/19`) as a *finish-and-archive* candidate rather than a fresh task.
   - Don't suggest anything in the `CLAIMED-BY` column — that's the whole point (no two brothers on one spec).
   - If I'm a feature brother already on a non-spec branch, note that before suggesting a switch.
   Then say how to take it: spin up a brother for it with `/newbrother -Branch <change-name>` (or, from a homestead, branch with the spec's exact name per CLAUDE.md), then `/opsx:apply <change-name>`.

4. If the helper errors or finds no changes, fall back: read `openspec/changes/` directly (skip `archive/`), compare each dir name against the brothers' branch leaves from `scripts\Get-Brothers.ps1`, and reason the same way.

This command is read-only — it reports and recommends; it doesn't create branches, edit tasks, or implement anything on its own.
