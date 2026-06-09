---
name: "New Brother"
description: Welcome a new brother — create a new sibling work folder checked out from dev
category: Worktrees
tags: [worktree, brothers, create]
---

Welcome a **new brother** — create his work folder under the family directory, started from `dev`. See [BROTHERS.md](../../BROTHERS.md) for the family convention.

Use this whenever you (or a brother) are told **"you have a new brother"**, **"welcome `<name>` to the team"**, or **"create a new brother"**.

> **Shell:** this is a Windows/PowerShell environment. Run the helper through the **PowerShell tool**, not Bash.

**Steps**

1. **Pick the name.** If the user named the brother, use that. Otherwise leave it blank and the script takes the next unused name from the roster in [BROTHERS.md](../../BROTHERS.md) (in order: `claude`, `wilhelm`, `friedrich`, `heinrich`, …), skipping any name that already has a folder.

2. **Know his task?** If the user said what the new brother will work on, pass a `-Branch` following the repo's branch rules (spec-named for an OpenSpec change, `{type}/{name}` otherwise — see [CLAUDE.md](../../CLAUDE.md)). If not, omit it — he's parked detached at `dev`'s tip, ready to branch when assigned. (He's never put *on* the `dev` branch itself: `dev` is the shared homestead branch other folders must be able to check out, and git allows a branch in only one worktree.)

3. **Run the helper:**
   ```powershell
   # next roster name, parked detached at dev's tip:
   powershell -NoProfile -File scripts\New-Brother.ps1
   # or with an explicit name and starting branch off dev:
   powershell -NoProfile -File scripts\New-Brother.ps1 -Name friedrich -Branch fix/oauth-callback
   ```
   It creates the folder as a `git worktree` (based off the `.bare` hub if present, else the `dev` homestead worktree, else this repo), starts him from `dev`, and prints his folder, base, state, and head commit.

4. **If the script can't run, fall back manually** (from the `dev` homestead if it exists, else this repo):
   ```powershell
   git -C <base> worktree add <family-dir>\<name> -b <branch> dev      # born to do work
   git -C <base> worktree add --detach <family-dir>\<name> dev         # just parked at dev's tip
   ```
   `<family-dir>` is the parent of `git rev-parse --show-toplevel`. **Never** `git worktree add <name> dev` (no `-b`, no `--detach`) — that puts the brother *on* the `dev` branch, and since git allows a branch in only one worktree it then blocks every other folder from checking out `dev`.

5. **Welcome him** — one or two sentences naming the new brother, his folder, and how he was started (detached at `dev`, or on which branch).

Creating a brother only touches the new folder — don't modify any existing brother's tree.
