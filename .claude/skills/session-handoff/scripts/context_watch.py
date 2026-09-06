#!/usr/bin/env python3
"""Warn the session when its context window is filling up.

Claude Code hooks receive a JSON object on stdin that names the session's
transcript file. Every assistant turn in that transcript carries a `usage`
record, and the sum of `input_tokens + cache_read_input_tokens +
cache_creation_input_tokens` on the LAST turn is what the model is actually
carrying right now -- which is the honest measure, unlike the file's size
(that keeps growing after a compaction while the window resets).

Past the threshold this prints a line the session sees, telling it to run the
`session-handoff` skill. It fires once per band (65 / 80 / 90) per session, so
a long session is nudged, then warned, then told plainly -- never spammed.

Exit code is always 0: a watcher must never block a tool call.
"""

import json
import os
import sys
from pathlib import Path

BANDS = (65, 80, 90)
TAIL_BYTES = 512 * 1024   # the last turn is at the end; never read a 12 MB log


def last_usage(path: str):
    """The most recent turn's token usage, read from the tail of the transcript."""
    try:
        size = os.path.getsize(path)
        with open(path, "rb") as fh:
            if size > TAIL_BYTES:
                fh.seek(size - TAIL_BYTES)
                fh.readline()          # drop the partial line the seek landed in
            lines = fh.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return None

    for line in reversed(lines):
        try:
            msg = (json.loads(line).get("message") or {})
        except Exception:
            continue
        usage = msg.get("usage")
        if usage:
            return usage
    return None


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return 0

    transcript = payload.get("transcript_path")
    session_id = payload.get("session_id") or "session"
    if not transcript:
        return 0

    usage = last_usage(transcript)
    if not usage:
        return 0

    used = (int(usage.get("input_tokens") or 0)
            + int(usage.get("cache_read_input_tokens") or 0)
            + int(usage.get("cache_creation_input_tokens") or 0))
    if used <= 0:
        return 0

    # The transcript records "claude-opus-5" for both the 200k and the 1M variant,
    # so the window is inferred from what we have already exceeded rather than
    # from the model name. An explicit override wins.
    window = int(os.environ.get("CLAUDE_CONTEXT_WINDOW") or 0)
    if window <= 0:
        window = 1_000_000 if used > 190_000 else 200_000
    pct = int(used * 100 / window)

    band = 0
    for b in BANDS:
        if pct >= b:
            band = b
    if band == 0:
        return 0

    # One warning per band per session.
    state_dir = Path(transcript).with_suffix("")
    state = state_dir.parent / f".context_watch_{session_id}.json"
    try:
        seen = json.loads(state.read_text(encoding="utf-8")).get("band", 0)
    except Exception:
        seen = 0
    if band <= seen:
        return 0
    try:
        state.write_text(json.dumps({"band": band, "pct": pct}), encoding="utf-8")
    except OSError:
        pass

    urgency = {
        65: "Wrap up what is in flight, then hand off.",
        80: "Hand off now; do not start anything new.",
        90: "Hand off immediately -- the next summarisation may lose detail.",
    }[band]
    note = (f"CONTEXT AT {pct}% ({used:,} of {window:,} tokens). {urgency} "
            f"Run the `session-handoff` skill: it writes docs/HANDOFF.md, commits the tree, "
            f"and tells the user how to start the next session.")

    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": payload.get("hook_event_name") or "PostToolUse",
            "additionalContext": note,
        }
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
