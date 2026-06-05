---
name: "Who am I"
description: Report this work folder's brother identity and what it's working on
category: Worktrees
tags: [worktree, brothers, identity]
---

Report **who you are** — the brother (work folder) you are operating from — and what you're currently working on. See [BROTHERS.md](../../BROTHERS.md) for the family convention.

> **Shell:** this is a Windows/PowerShell environment. Run every command below through the **PowerShell tool**, not Bash — PowerShell `if (...) { ... }` and `;`-separated statements fail under bash. You can batch all the read-only git calls into one PowerShell invocation.

**Steps**

1. **Identity.** Run `git rev-parse --show-toplevel` and take the last path segment. That leaf is your **brother name** — one of the names on the roster in [BROTHERS.md](../../BROTHERS.md) (including `claude`, the eldest/default brother — don't exclude it as "not German"). If it is `dev` or `app`, you are at the **homestead** (the shared integration/production clones), not a feature brother — say so.

2. **Current task.** Gather:
   - Branch: `git rev-parse --abbrev-ref HEAD`
   - Last commit: `git log -1 --format='%s (%cr)'`
   - Tree state: `git status --porcelain` (count the lines = uncommitted changes)
   - Focus note: if a `.brother-status` file exists in the repo root, read it.

3. **Answer in first person, one or two sentences.** Example:
   > I'm **wilhelm**, working branch `cancel-superseded-runs` (last commit "ci: cancel superseded workflow runs…", 2m ago; 1 uncommitted change). Focus: *<.brother-status note, if any>*.

4. If the branch name matches an OpenSpec change under `openspec/changes/`, mention which change you're implementing.

Do not modify anything — this is read-only reporting.
