---
name: session-handoff
description: Close out a vibegame1 session cleanly and hand it to the next one — write docs/HANDOFF.md, get the tree committed and verified, and tell the user exactly how to start fresh. Use this the moment the context watcher reports 65% or more, and whenever the user says "handoff", "wrap up", "context is filling up", "start a new session", "I'm going to clear", "save state before we lose it", or asks what the next session should pick up. Also use it before any deliberate /clear or /compact, and at the end of a long working session even if nobody mentions context — a session that ends without a handoff costs the next one an hour of rediscovery.
---

# Session handoff

A session ends whether you plan it or not. The difference between a handoff and a
crash is whether the next session opens knowing what is half-built, what is
unproven, and what the user was about to say next.

`docs/HANDOFF.md` is the project's own contract for this: CLAUDE.md points every
new session at it, so it is the one file guaranteed to be read. Everything below
serves getting that file honest and the tree safe.

## When this fires

The watcher (`scripts/context_watch.py`, wired as a `PostToolUse` hook) prints
`CONTEXT AT n%` once per band — 65, 80, 90. Treat them differently:

- **65%** — finish the piece of work in your hands, then hand off. Do not start
  a new feature or a new subagent lane.
- **80%** — hand off now. Anything unfinished goes into the handoff as words
  rather than into the code as a half-edit.
- **90%** — hand off immediately. Past here a summarisation may drop the details
  the next session needs, and a handoff written from a summary is a guess.

You can also be asked for a handoff at any time. Same procedure.

## The procedure

Work top to bottom. It usually takes five minutes; skipping the verification
steps is what makes a handoff a lie.

### 1. Find out what is actually true

Never write the handoff from memory of what you intended. Read the tree:

```bash
git status --short && git log --oneline -12
```

Then account for **every** uncommitted file. A file you cannot explain is the
single most valuable thing to flag, because it is the one the next session will
misread.

### 2. Land the tree

Uncommitted work is the thing most likely to be lost, so it goes first.

- Compiles at minimum: `dotnet build Assembly-CSharp.csproj` and
  `Assembly-CSharp-Editor.csproj` at the repo root. Never hand over a tree that
  does not build without saying so in capital letters.
- If the editor is free (check `is_playing` first — see the `unity-editor`
  skill), run the suites and record the real numbers, not yesterday's.
- Commit in the project's units: one commit per coherent change, and one commit
  per subagent pass prefixed with the team's name (`[ui-designer] …`), which is
  the CLAUDE.md regression guard. A regression must stay revertible alone.
- If something genuinely cannot be committed — a broken experiment worth keeping
  — say so explicitly in the handoff and leave it uncommitted rather than
  burying it in an unrelated commit.

### 3. Rewrite docs/HANDOFF.md

Rewrite it; do not append. It describes a moment, and a handoff carrying six
sessions of history is one nobody finishes reading. Keep the newest state at the
top under `## What happened`, and keep these sections honest:

- **What happened** — this session's work in a short paragraph, newest first,
  naming the systems and the commits. Lead with anything that changes how the
  project behaves.
- **State of the tree** — committed vs not, the tag or sha to revert to, which
  generators have been run since the last code change (this is the one people
  forget, and stale prefabs present as impossible bugs).
- **Verification** — the actual suite numbers with their date, and which of them
  you ran yourself versus inherited. Say plainly what is unproven and what only
  a human playtest can settle.
- **Do first next session** — three items at most, ordered, each with enough
  context to start without asking. This is the section that earns the file.
- **Open questions for the user** — decisions you could not make. Five at most.

Two habits that make a handoff trustworthy: convert relative dates to absolute
ones (*"tomorrow"* is meaningless to the next session), and separate what you
verified from what you assumed. A confident sentence about something you did not
check is worse than no sentence.

### 4. Carry the in-flight work

Anything mid-air needs a home before you go:

- **Subagents still running** — say what was delegated and where their output
  file is; the next session cannot receive their notifications.
- **A plan or research doc** — the project keeps these under `docs/plans/` and
  `docs/research/`. Point at the file rather than restating it.
- **Something the user asked for that you did not do** — put it in the handoff
  AND in `docs/BACKLOG.md` with its intended shape. Backlog items get built;
  chat requests get lost. (This is not hypothetical: the slide's stamina cost
  sat in the backlog unbuilt while the user asked for it repeatedly.)
- **Anything worth remembering across projects** — a user preference, a rule
  they set, a trap that cost an hour — belongs in the memory directory, not only
  in the handoff.

### 5. Hand over

Tell the user, in a few lines: what landed, what is unproven, what to do first,
and how to continue. Be straight about the last part — **a session cannot start
another session.** What is actually available:

- `/clear` — same terminal, fresh context. The usual choice, and the next
  session reads CLAUDE.md and `docs/HANDOFF.md` on the way in.
- `/compact` — keeps a summary instead. Cheaper, but a summary of a summary is
  how detail dies; prefer `/clear` once a real handoff exists.
- A new `claude` invocation in the project directory — same result as `/clear`
  with a clean terminal.

Then stop. Do not begin new work in the tail of a session you have just declared
full.

## What good looks like

A handoff succeeds if a session that has never seen this conversation can open
`docs/HANDOFF.md`, run the do-first list, and not need to ask the user anything
that was already known. When you have finished writing it, read it once as that
stranger. The sentences that only make sense because you were here are the ones
to fix.
