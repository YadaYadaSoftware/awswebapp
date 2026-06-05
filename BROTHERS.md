# BROTHERS.md — parallel work folders

This repo is worked from **multiple sibling folders at once**, so several features can be in flight in parallel without stepping on each other. Each folder is a separate checkout (a `git worktree` or a full clone) of the same GitHub repo, sitting side-by-side under the **family directory**:

```
C:\Users\hound\source\repos\YadaYadaSoftware\awswebapp\   <- the family directory
├─ app        # homestead: production clone, always on branch `app`
├─ dev        # homestead: integration clone, always on branch `dev`
├─ wilhelm    # a brother: feature work folder on its own branch
├─ friedrich  # a brother: another feature, another branch
└─ …
```

## The metaphor

Each work folder is a **brother** in a family of Germans. The folder's name **is** the brother's first name, and that name is your **identity** while you work there. A brother works on exactly one branch at a time — so:

- **Who am I?** → the leaf name of the folder I'm running in (e.g. `wilhelm`).
- **What am I working on?** → the branch checked out in this folder, plus its latest commit.
- **What are my brothers doing?** → the branches + latest commits in the *other* sibling folders.

`app` and `dev` are not brothers — they are the **homestead**: the shared, long-lived clones for production (`app`) and integration (`dev`). Everyone branches from `dev` and merges back through the normal flow.

## The name pool

Brothers are named, in order, from this roster of German first names. Take the next unused name when you create a new work folder:

`claude`, `wilhelm`, `friedrich`, `heinrich`, `karl`, `otto`, `ludwig`, `ernst`, `hermann`, `konrad`, `albrecht`, `bruno`, `emil`, `felix`, `klaus`, `werner`, `dietrich`, `gunther`, `lothar`, `bernhard`

(`claude` is the eldest — the default brother. The rest are added as parallel work demands.)

## Creating a new brother

When you're told **"you have a new brother"**, **"welcome `<name>` to the team"**, or **"create a new brother"**, the job is: pick his name, create his folder, and check him out from `dev`. The helper does all three:

```powershell
# Next unused roster name, checked out on dev:
powershell -NoProfile -File scripts\New-Brother.ps1
# …or name him and give him a starting branch off dev:
powershell -NoProfile -File scripts\New-Brother.ps1 -Name friedrich -Branch fix/oauth-callback
```

[scripts/New-Brother.ps1](scripts/New-Brother.ps1) takes the next unused name from the roster above (or `-Name`), creates the folder as a `git worktree` based off the `dev` homestead clone if present (else off the current repo), and starts him from `dev` — a fresh branch off dev with `-Branch`, or dev itself when no branch is known yet. Also exposed as the `/newbrother` slash command.

Or do it by hand from the `dev` homestead (lightweight worktree that shares the object store):

```powershell
# New feature branch off dev, checked out into a new brother folder:
git -C C:\Users\hound\source\repos\YadaYadaSoftware\awswebapp\dev `
    worktree add ..\wilhelm -b <branch-name> dev

# …or check out an EXISTING branch into a new brother folder:
git -C ...\dev worktree add ..\friedrich <existing-branch>
```

> **Note:** git allows the `dev` branch to be checked out in only one worktree of a clone. If `dev` is already checked out in the base clone, a new worktree can't sit literally on `dev` — start him on a branch off dev (`-b … dev`) or detached at dev's tip (`--detach … dev`); the helper handles this automatically.

A full `git clone` into a sibling folder works too (this is how `app`/`dev` are set up); the tooling below treats clones and worktrees identically.

> **Branch vs. brother:** the brother name (the folder) is *who*; the branch is *what*. They are independent. `wilhelm` might be on branch `cancel-superseded-runs`. Follow the repo's branch rules in [CLAUDE.md](CLAUDE.md) (spec-named branches for OpenSpec changes, `{type}/{name}` otherwise).

## The family questions

- **`/whoami`** — reports the current brother's identity, branch, last commit, and tree state.
- **`/brothers`** — reports every *other* brother's branch, last commit, tree state, and any status note.
- **`/newbrother`** — welcomes a new brother: picks his name, creates his folder, checks him out from `dev` (see [Creating a new brother](#creating-a-new-brother)).
- **`/next`** — tells you the next step on your spec, or — if you're idle — suggests an OpenSpec change **no brother is currently on**, so the family doesn't double up on the same spec.

All four are also answered in plain conversation: just ask *"who are you?"*, *"what are my brothers doing?"*, *"you have a new brother"*, or *"what should I work on next?"* and Claude follows the same procedure (see [CLAUDE.md](CLAUDE.md) → "Parallel work folders").

Under the hood each is a script in [scripts/](scripts/): [Get-Brothers.ps1](scripts/Get-Brothers.ps1), [New-Brother.ps1](scripts/New-Brother.ps1), and [Get-NextStep.ps1](scripts/Get-NextStep.ps1). `Get-Brothers.ps1` and `Get-NextStep.ps1` share their sibling-enumeration logic via [scripts/_BrothersCommon.ps1](scripts/_BrothersCommon.ps1) — one place scans the family directory and reads each checkout's git state.

## Leaving a note for the family (optional)

A brother can write a one-line free-text note about their current focus to a `.brother-status` file in their folder root. It is **gitignored** (per-folder, never committed) and is surfaced by `/brothers` and `/whoami`:

```powershell
"Refactoring the deploy concurrency block; blocked on a CI run" | Set-Content .brother-status -Encoding utf8
```
