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

From the `dev` homestead (recommended — uses a lightweight worktree that shares the object store):

```powershell
# New feature branch off dev, checked out into a new brother folder:
git -C C:\Users\hound\source\repos\YadaYadaSoftware\awswebapp\dev `
    worktree add ..\wilhelm -b <branch-name> dev

# …or check out an EXISTING branch into a new brother folder:
git -C ...\dev worktree add ..\friedrich <existing-branch>
```

A full `git clone` into a sibling folder works too (this is how `app`/`dev` are set up); the tooling below treats clones and worktrees identically.

> **Branch vs. brother:** the brother name (the folder) is *who*; the branch is *what*. They are independent. `wilhelm` might be on branch `cancel-superseded-runs`. Follow the repo's branch rules in [CLAUDE.md](CLAUDE.md) (spec-named branches for OpenSpec changes, `{type}/{name}` otherwise).

## Answering the two questions

- **`/whoami`** — reports the current brother's identity, branch, last commit, and tree state.
- **`/brothers`** — reports every *other* brother's branch, last commit, tree state, and any status note.

Both are also answered in plain conversation: just ask *"who are you?"* or *"what are my brothers doing?"* and Claude follows the same procedure (see [CLAUDE.md](CLAUDE.md) → "Parallel work folders").

Under the hood `/brothers` runs [scripts/Get-Brothers.ps1](scripts/Get-Brothers.ps1), which scans the family directory for sibling checkouts and queries each with `git -C`.

## Leaving a note for the family (optional)

A brother can write a one-line free-text note about their current focus to a `.brother-status` file in their folder root. It is **gitignored** (per-folder, never committed) and is surfaced by `/brothers` and `/whoami`:

```powershell
"Refactoring the deploy concurrency block; blocked on a CI run" | Set-Content .brother-status -Encoding utf8
```
