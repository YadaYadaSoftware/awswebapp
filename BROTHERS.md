# BROTHERS.md — parallel work folders

This repo is worked from **multiple sibling folders at once**, so several features can be in flight in parallel without stepping on each other. All folders are **`git worktree`s of one shared bare repository** (`.bare`) sitting side-by-side under the **family directory**:

```
C:\Users\hound\source\repos\YadaYadaSoftware\awswebapp\   <- the family directory
├─ .bare      # the hub: a bare repo holding all history. No working tree; you never cd here.
├─ app        # homestead worktree, always on branch `app`     ┐
├─ beta       # homestead worktree, always on branch `beta`     │ shared, long-lived
├─ alpha      # homestead worktree, always on branch `alpha`    │
├─ dev        # homestead worktree, always on branch `dev`     ┘
├─ wilhelm    # a brother: feature work folder on its own branch
├─ friedrich  # a brother: another feature, another branch
└─ …
```

**Why a `.bare` hub?** Every folder shares one object store, so **merging is local and instant** — from `dev` you just `git merge <brother-branch>` with no push/pull round-trip through GitHub. And because no *working* folder owns the history, you can delete any folder (even `claude`) without harming the others. The one git rule to remember: **a branch can be checked out in only one worktree at a time** — which is exactly why each shared branch gets its own homestead folder and a brother never sits *on* `dev`. (A full `git clone` per folder also works, but then cross-folder merges have to round-trip through origin — the `.bare` layout is preferred.)

## The metaphor

Each work folder is a **brother** in a family of Germans. The folder's name **is** the brother's first name, and that name is your **identity** while you work there. A brother works on exactly one branch at a time — so:

- **Who am I?** → the leaf name of the folder I'm running in (e.g. `wilhelm`).
- **What am I working on?** → the branch checked out in this folder, plus its latest commit.
- **What are my brothers doing?** → the branches + latest commits in the *other* sibling folders.

`app`, `beta`, `alpha`, and `dev` are not brothers — they are the **homestead**: the shared, long-lived worktrees for production (`app`), the staged infra branches (`beta`/`alpha`), and integration (`dev`). Everyone branches from `dev` and merges back through the normal flow. `/whoami` and `/brothers` treat these four as homestead, not feature brothers.

## The name pool

Brothers are named, in order, from this roster of German first names. Take the next unused name when you create a new work folder:

`claude`, `wilhelm`, `friedrich`, `heinrich`, `karl`, `otto`, `ludwig`, `ernst`, `hermann`, `konrad`, `albrecht`, `bruno`, `emil`, `felix`, `klaus`, `werner`, `dietrich`, `gunther`, `lothar`, `bernhard`

(`claude` is the eldest — the default brother. The rest are added as parallel work demands.)

## Creating a new brother

When you're told **"you have a new brother"**, **"welcome `<name>` to the team"**, or **"create a new brother"**, the job is: pick his name, create his folder, and start him from `dev`. The helper does all three:

```powershell
# Next unused roster name, parked detached at dev's tip:
powershell -NoProfile -File scripts\New-Brother.ps1
# …or name him and give him a starting branch off dev:
powershell -NoProfile -File scripts\New-Brother.ps1 -Name friedrich -Branch fix/oauth-callback
```

[scripts/New-Brother.ps1](scripts/New-Brother.ps1) takes the next unused name from the roster above (or `-Name`), creates the folder as a `git worktree` of the `.bare` hub (falling back to the `dev` worktree or the current repo if there's no `.bare`), and starts him from `dev` — a fresh branch off dev with `-Branch`, or **detached at dev's tip** when no branch is known yet. Also exposed as the `/newbrother` slash command.

> **A brother never sits *on* the `dev` branch itself.** `dev` is the shared homestead branch every folder must be able to check out, and **git allows a given branch in only one worktree of a clone** — so if one brother occupied `dev`, no other folder (including the homestead) could check it out, and `git checkout dev` would fail with *"'dev' is already used by worktree at …"*. That's why a branch-less brother is parked **detached** at dev's tip (he gets dev's files without owning the branch) and gets his own branch the moment he's assigned work. The helper enforces this; by hand, use `-b <branch> dev` or `--detach dev`, never a bare `… dev`.

Or do it by hand from the `.bare` hub:

```powershell
$bare = "C:\Users\hound\source\repos\YadaYadaSoftware\awswebapp\.bare"

# New feature branch off dev, checked out into a new brother folder:
git -C $bare worktree add ..\wilhelm -b <branch-name> dev

# …or park him detached at dev's tip (no branch yet):
git -C $bare worktree add --detach ..\friedrich dev
```

Retire a brother with `git -C $bare worktree remove ..\<name>` (make sure his branch is pushed or merged first — a worktree of `.bare` shares the hub's history, but uncommitted work in the folder is lost on removal).

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
