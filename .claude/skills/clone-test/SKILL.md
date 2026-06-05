---
name: clone-test
description: Send the current working-tree changes to the isolated Unity test clone as a queued job. Use when the user says "/clone-test", "clone test", "send this to the clone", "queue a test", or wants to test the current change in the separate Unity session without disturbing the main editor. Pairs with the TestCloneKit Unity plugin (Tools ▸ Test Clone), which drains the queue and auto-plays each job.
---

# clone-test

Snapshot the project's current changes into a **queued job** that the isolated Unity **test clone** picks up and runs
(applies the files, recompiles, then enters Play). This lets the user validate one change in isolation while you keep
editing the main project. Each invocation creates one independent, numbered job, so several can be queued and the clone
drains them one at a time.

This is the producer half. The consumer is the **TestCloneKit** Unity plugin (`Tools ▸ Test Clone`), open in the clone
editor with "Run Next" or "Auto-drain" enabled.

## How to run it

Run the enqueue script from the repo root (its working directory is already the project root):

```
powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/clone-test/scripts/enqueue.ps1 -Label "<short-label>" -Action play -Session "<session-id>"
```

- **`-Label`** — a short, file-safe label describing the change (e.g. `bot-aim-fix`). Derive it from the user's request
  or the files changed. Default `job`.
- **`-Action`** — what the clone does after applying the snapshot:
  - `play` (default) — enter Play mode so it's tested immediately.
  - `recompile` — just apply + compile, let the user press Play.
  - `menu:<Menu/Path>` — apply, then execute an editor menu item (e.g. a project's own test launcher).
- **`-Filter`** — optional substring to include only matching changed paths (e.g. `-Filter BotBrain` to send just that
  file). Omit to send **all** current changes.
- **`-Session`** — a tag shown in the clone's queue list identifying who queued it. Use something stable for this
  session if known; otherwise `claude`.

## Steps

1. Decide the label (and filter, if the user named a specific file/area) from the user's request.
2. Run the script via the Bash/PowerShell tool with those arguments.
3. Report back the job id the script prints, and remind the user the clone will pick it up via **Run Next** / **Auto-drain**.

Do **not** try to control the Unity editor yourself — the queue is the hand-off. If the script reports "No changes to
queue", tell the user there's nothing modified to send.

## Notes

- The queue lives at `<repo>/.clone-test-queue/` (git-ignored). It's safe to leave it; processed jobs move to `done/`.
- Jobs snapshot the **file bytes at enqueue time**, so committing or further edits afterward don't change an already-queued job.
- If the user hasn't created the clone yet, point them to `Tools ▸ Test Clone ▸ Create Clone` in the main editor.
