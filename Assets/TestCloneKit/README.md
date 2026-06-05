# Test Clone Kit

A standalone Unity editor plugin that runs a **second, isolated Unity session** alongside your main one and lets you
**copy only the changes you choose** into it. Test one thing in isolation while other tools — or AI agents driving the
editor over MCP — keep working the main project.

It's built on a **git worktree**: a sibling folder that shares your repo's `.git` but has its own files and its own
`Library`. That independence is the whole point — unlike a symlink-based clone (e.g. ParrelSync), nothing propagates
automatically, so *you* decide what reaches the test session.

## Requirements

- Unity 2021.3+ (uses only `UnityEngine` / `UnityEditor`).
- `git` on your PATH, and the project must live inside a git repository.

No other dependencies. Editor-only — nothing ships in a build.

## Install

Copy the `TestCloneKit/` folder into your project's `Assets/` (or `Packages/`). That's it.

To use it as a UPM package instead, move the folder under `Packages/` or reference it via a local path in
`Packages/manifest.json`:

```json
"com.gard.testclonekit": "file:../path/to/TestCloneKit"
```

## Usage

Open **Tools ▸ Test Clone**.

1. **Create Clone** — creates a git worktree at a sibling folder (default `<repo>-TestClone`) on its own branch
   (default `test-clone`), checked out at your current commit. One-time.
2. **Open in Unity** — launches a second editor on the clone. The first open does a full asset import; give it a minute.
3. **Refresh Changes** — lists every file modified in your working tree (`git status --porcelain`).
4. Tick the files you want, then **Sync Selected → Clone**. Only those files are copied in (renames, deletions, and
   `.meta` sidecars handled). The clone reimports when you focus its window.

**Remove Clone** tears down the worktree (the branch is kept).

## Job queue (hands-off / agent-driven)

Instead of picking files by hand, you can **queue jobs** that the clone drains one at a time. A job is a self-contained
snapshot of chosen files plus a manifest, dropped under `<repo>/.clone-test-queue/pending/`. The clone's **Job queue**
section lists them; **Run Next** applies the next job's files, recompiles, then runs its action (default: enter Play).
Turn on **Auto-drain** to process jobs the moment they arrive, hands-free.

Because each job snapshots the file bytes at enqueue time, you can queue several in a row and they never clobber each
other — the clone just goes Next.

### Queuing from Claude Code (the `/clone-test` skill)

This repo ships a paired Claude Code skill at `.claude/skills/clone-test/`. From any Claude session working the main
project, `/clone-test <label>` snapshots the current changes into a queued job. The clone (with Auto-drain on, or via Run
Next) picks it up and plays it — so you can keep working in one session while another's change is tested in isolation.

You can also enqueue manually:

```
powershell -File .claude/skills/clone-test/scripts/enqueue.ps1 -Label my-change -Action play
```

`-Action` is `play` (default), `recompile`, or `menu:<Menu/Path>`; `-Filter <substring>` narrows which changed files go.

The queue location is shared via the **main** worktree, so the clone reads it no matter which folder it lives in. The
`.clone-test-queue/` folder is git-ignored.

## Keeping the clone in sync (baseline)

The clone is created from a commit and lives on its own branch, so as **main** advances, the clone's *baseline* (everything
you didn't explicitly sync) goes stale — and tests there stop reflecting main. The **Baseline** section of the window
shows how many commits the clone is behind and offers **Re-baseline clone → main**, which resets the clone's tracked
files to main's current HEAD (`git reset --hard`). It warns first if the clone has unsynced edits (which a reset would
discard — pull them back first). Re-baselining also brings the clone's copy of this plugin back in step with main.

Push (sync/queue) handles in-progress *uncommitted* changes; re-baseline handles *committed* drift. Between the two, the
clone never silently diverges from main.

## Pulling clone edits back to main

The clone is a normal git worktree, so anything you change *inside* it (tweak the scene, adjust values, edit a script)
is tracked on its `test-clone` branch. To bring those edits back, open **Tools ▸ Test Clone** in the **main** editor and
use the **Changes made in the clone** section:

1. **Refresh from Clone** — lists files changed in the clone's working tree (`git status` run against the clone).
2. Tick the ones you want, then **Pull Selected → Main (working tree)**.
3. The files are copied into the main project as **uncommitted** changes. Review the diff and commit in main normally.

Pulling overwrites the matching files in main, so do it when you're ready to integrate — and consider pausing any agents
editing the main project first. This section only appears in the main editor (it pulls *into* the project you have open).

### Working alongside AI agents / MCP

If agents drive your main editor via a Unity MCP server, both editors register as separate instances. Pin agents to the
**main** instance so their traffic never disturbs the clone you're testing in.

## How it works

- `TestCloneService` — all git, filesystem, and settings logic. Discovers the repo with `git rev-parse --show-toplevel`,
  so it works whether the Unity project is at the repo root or in a subfolder. Settings persist in `EditorPrefs` keyed by
  repo path (never committed).
- `TestCloneWindow` — a plain IMGUI `EditorWindow`.

Sync is a deliberate file copy from your working tree into the clone — not a commit and not a symlink — which is what
gives you per-file control over what the isolated session sees.

## License

Do as you like. Provided as-is, no warranty.
