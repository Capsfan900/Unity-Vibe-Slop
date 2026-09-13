---
name: astra-engineering-company
description: Coordinate substantial vibegame1 engineering work as a cost-aware hierarchy: Astra retains architecture and integration, capable implementation models own coherent lanes, and cheaper workers handle bounded searches and checks. Use for multi-part work that benefits from delegation; skip for small, tightly coupled edits where coordination would cost more than doing the work directly.
---

# Astra engineering company

Use several agents to reduce elapsed time and expensive-model context, while keeping one mind responsible for the whole result. This is a plain-Markdown operating protocol: translate its roles to the models and delegation mechanism available in the current coding-agent harness. If spawning is unavailable, execute the same protocol sequentially in one agent while preserving the context-packet and report boundaries.

## Authority

Astra is a role, not a model — it is held by whichever frontier model the user is running as lead for the
session (Astra, Fable, Opus, or another). Every commit records both: subject prefix `[<role>]`, body trailer
`Model: <actual model> (<harness>)`. The root lead is that model when available, otherwise the strongest
available reasoning/coding model. The lead owns:

- the user's actual intent, scope, architecture, and irreversible decisions;
- the work graph, file ownership, integration, live Unity editor, final verification, and user report;
- every decision that crosses lanes or changes a project contract.

Map workers by capability, not brand name:

| Tier | Preferred model | Work |
|---|---|---|
| Lead | Astra | architecture, decomposition, integration, ambiguous debugging, final review |
| Senior | strongest Sol | coherent implementation lane, difficult diagnosis, substantive review |
| Engineer | Terra | bounded implementation, tests, docs, regression analysis |
| Utility | Luna | read-only searches, inventories, comparisons, and evidence gathering |

If exact models are unavailable, preserve the capability ordering. Never claim a model was used when the harness could not select it.

## Decide whether to delegate

The lead first forms a compact mental model of the request and identifies the project contracts it touches. Delegate only when a lane is independently useful, has a crisp acceptance condition, and will save lead context or wall time. Three or more disjoint parts normally justify a team; two justify it only when they are slow, specialized, or can make meaningful progress in parallel.

Do the work directly when it is a small edit, depends on rapid back-and-forth in one file, requires less effort than briefing and reviewing a worker, or needs the lead's exclusive Unity-editor access.

Prefer the cheapest tier that can complete the lane without making product or architecture decisions. Promote the lane when evidence shows ambiguity or cross-system judgment; do not keep retrying an underpowered worker. Parallelism is a budget, not a target. Start only lanes that can proceed without overlapping writes.

Utility workers are read-only by default. Move a task to the Engineer tier before allowing repository edits, even when the edit appears mechanical.

## Create the work graph

Before spawning workers:

1. Read `AGENTS.md` and only the task-specific documents it routes to.
2. Separate decisions, implementation lanes, read-only investigation, and final integration.
3. Create a lead-owned manifest of every claimed path, its owner, and read/write mode. A worker does not edit until its paths are claimed. Shared files remain with the lead or a single integration lane.
4. Batch work that needs Unity into one final editor pass.
5. Keep a useful lead task in progress while workers run.

Shared documentation and integration files are lead-owned by default. Workers return proposed wording with source locations instead of editing them. For a broad read-only audit, prefer one utility worker that builds an indexed evidence map before deciding whether specialist readers are worth their context cost.

Every worker receives this context packet:

```text
Outcome: one observable result.
Why: the local purpose, in one or two sentences.
Evidence to read: exact files, symbols, or one routed document.
Owned files: exact writable files/directories; "none" for research.
Relevant invariants: only rules that affect this lane.
Forbidden: decisions and files reserved for the lead or other lanes.
Acceptance: commands, checks, or evidence that prove the result.
Return: conclusion first; changed files; verification; remaining uncertainty; editor/generator steps needed.
```

Do not dump the whole conversation into a utility worker when this packet is sufficient. Do not hide essential product intent merely to shorten the packet.

## Operate and integrate

- Workers inspect the current tree immediately before editing and preserve unrelated user or agent changes.
- A worker stops and reports when its task requires a forbidden file, a new mechanic or product decision, live-editor control, destructive action, or a contract change outside its brief.
- Research workers return conclusions with `file:line` evidence, not pasted file contents.
- Editing workers make only their owned change and run the cheapest meaningful offline check. They never commit.
- The lead reviews actual diffs and repository state; worker summaries are navigation aids, not proof.
- On conflict or inconsistent recommendations, the lead resolves from user intent and project evidence. Do not ask two more agents to vote.
- After integration, the lead runs the required generators and verification once for the batch, updates invalidated documentation, and owns any commit or handoff. Apply any project adapter's exact tag, commit grouping, and prefix rules.

Do not multiply agents recursively unless the harness supports it and a child has a genuinely independent sub-lane. The lead remains accountable even when delegation nests.

## Project adapter

For any edit or verification in this repository, read [references/vibegame1.md](references/vibegame1.md). It translates the company roles into this project's authorship boundaries, specialist briefs, Unity ownership, and regression rules.
