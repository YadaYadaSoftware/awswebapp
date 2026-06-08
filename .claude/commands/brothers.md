---
name: "Brothers"
description: Report what the other brothers (sibling work folders) are working on
category: Worktrees
tags: [worktree, brothers, teammates]
---

Report what your **brothers** — the other work folders under the family directory — are currently working on. See [BROTHERS.md](../../BROTHERS.md) for the family convention.

> **Shell:** this is a Windows/PowerShell environment. Run the helper and any fallback git commands through the **PowerShell tool**, not Bash.

**Steps**

1. **Run the helper:**
   ```powershell
   powershell -NoProfile -File scripts\Get-Brothers.ps1
   ```
   It scans the family directory (the parent of this repo root) for every sibling checkout — clone or `git worktree` — and prints each brother's branch, tree state, ahead/behind vs origin, last commit, and any `.brother-status` note. The current brother is omitted by default (pass `-IncludeSelf` to include it).

2. **If the script can't run, fall back to doing it manually:**
   - family dir = parent of `git rev-parse --show-toplevel`
   - for each sibling subfolder that contains a `.git` entry, run:
     `git -C <folder> rev-parse --abbrev-ref HEAD`,
     `git -C <folder> log -1 --format='%s (%cr)'`,
     `git -C <folder> status --porcelain`

3. **Summarize for the user** — one line per brother, excluding yourself. Lead with the brother's name and branch. Call out anyone whose branch matches an OpenSpec change name (`openspec/changes/<branch>/`) and anyone with a dirty tree or unpushed commits.

4. Note that `app` and `dev` are the **homestead** (shared production/integration clones), not feature brothers — list them separately or label them as such.

Do not modify anything in any folder — this is read-only reporting.
