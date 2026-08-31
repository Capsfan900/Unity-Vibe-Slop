# Ghost racing and leaderboards

**Status:** implemented, local-only. Phase 1 of [multiplayer-system-design.md](multiplayer-system-design.md).
**No networking exists.** Nothing here talks to a server, and nothing needs to.

This is the design doc's own first recommendation (§2b): asynchronous ghost racing carries the social hook —
*"you are 0.4 s behind"* — at a fraction of co-op's cost, and with **zero exposure to the 130 ms parry-latency
problem** that makes live co-op risky. Combat code is untouched by this feature.

---

## What it does

Finish the level and your run is recorded. Next attempt, a translucent ghost of your personal best runs the
course beside you in real time, with a live delta under the timer, and a leaderboard of your best times in the
top-right.

| | |
|---|---|
| Records | Position, yaw, pitch, state flags, equipped weapon — at a fixed 30 Hz tick |
| Ghost | Translucent figure driven by the **same run clock**, so it is a race, not a replay |
| Delta | `-0.62s` green = ahead of PB · `+1.14s` red = behind |
| Leaderboard | Best 10 runs per level, kept on disk |
| Backend | `LocalLeaderboardBackend` — swappable for a remote one without touching call sites |

---

## Architecture

```
SpeedrunTimer ──RunStarted/RunFinished──> RunRecorder ──GhostRecording──> GhostRacing
                                                                              │
                                              Leaderboard ──ILeaderboardBackend──> RunStore ──> disk
                                                                              │
                                              GhostPlayer <──GhostRecording───┘
                                                   │
                                              GhostHud (delta + table)
```

| File | Role |
|---|---|
| `Assets/Scripts/Ghost/GhostData.cs` | `GhostSample`, `GhostRecording`, `RunEntry`, `RunIndex`, `GhostFlags` |
| `Assets/Scripts/Ghost/RunRecorder.cs` | Samples the run on a fixed tick |
| `Assets/Scripts/Ghost/GhostPlayer.cs` | Builds and drives the translucent ghost; computes the delta |
| `Assets/Scripts/Ghost/RunStore.cs` | Disk persistence — index file + one blob per run |
| `Assets/Scripts/Ghost/Leaderboard.cs` | `ILeaderboardBackend`, `LocalLeaderboardBackend`, the model |
| `Assets/Scripts/Ghost/GhostHud.cs` | Self-building canvas: delta + table |
| `Assets/Scripts/Ghost/GhostRacing.cs` | Orchestrator, self-bootstrapping |

### Three decisions worth knowing

**1. It records transforms, not input.**
A visual ghost only needs to know where you were — transform playback *cannot* desync, because there is nothing
being simulated. The design doc's input-stream recording (§7.5) is a **server-side anti-cheat** concern, and it
could never be a renderer here anyway: Unity's `CharacterController` and NavMesh are not deterministic across
machines (§10, explicit non-goal). When the backend lands, input recording is added **alongside** this, not
instead of it.

**2. It self-bootstraps.**
`GhostRacing` installs itself via `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` if a `SpeedrunTimer` is
present. Nothing was added to any prefab or to the level builder, so a `4. Build Prefabs` or `6. Build Level`
rebuild can never drop the feature, and removing it means deleting one folder. The HUD builds its own canvas
for the same reason — it is immune to a HUD rebuild and cannot collide with `HUDController`.

**3. The delta is measured by position, not by clock.**
Comparing elapsed times directly would always read zero — the ghost and the player share one clock. Instead a
cursor walks forward along the ghost's recorded path to the sample nearest your current position, giving *"how
long the ghost took to get where I am"*. The cursor only ever advances, so circling an enemy does not make the
delta jitter.

> **Limitation:** this is correct because the level is a linear course. A branching or looping level would need
> checkpoint-based splits instead. The delta hides itself when you are more than 6 m off the recorded path
> (different route, or you fell).

---

## Data format and size

One sample, as stored:

```json
{"x":123,"y":1800,"z":4560,"a":1234,"p":-123,"f":5,"w":0}
```

| Field | Meaning | Quantisation |
|---|---|---|
| `x` `y` `z` | World position | centimetres (int) |
| `a` | Yaw | tenths of a degree, 0–3600 |
| `p` | Pitch | tenths of a degree, −900–900 |
| `f` | `GhostFlags` bitfield | grounded / dashing / attacking / parrying / staggered / executing / drinking / dead |
| `w` | Equipped weapon index | int |

There is **no timestamp**. Samples sit on an exact fixed tick, so `time = index / tickHz`. That saves ~11 bytes
per sample and makes it impossible for a recording's clock to drift out of step with its own indices.

Everything is quantised to integers because `JsonUtility` writes floats at full precision — `1.2339999` is nine
characters where `123` is three. This is what keeps a plain-JSON ghost inside the size budget without a binary
format.

### Size

| | |
|---|---|
| Per sample, on disk | **~60 bytes** of JSON |
| Per second @ 30 Hz | **~1.7 KB/s** |
| **Per minute** | **~105 KB** |
| A 2-minute run | ~210 KB |
| In memory | 28 B/sample → ~50 KB/minute |

The design doc budgets ghosts at 50–300 KB *compressed*; this sits in that range **uncompressed**, so gzip at
upload time would leave comfortable headroom. Compression is deliberately not implemented yet — there is no
upload to compress for.

Lower `tickHz` on `RunRecorder` if size ever matters; playback interpolates, so 15 Hz still looks smooth.

### On disk

`Application.persistentDataPath`:

```
runs_Level_01.json      leaderboard index — metadata rows only, small, read constantly
ghost_<id>.json            one recording blob per run — large, read only when a ghost loads
```

That split is not incidental: it is the shape of the eventual backend (design doc §6 — Postgres for
leaderboard rows, S3-compatible object storage for ghost blobs). Reading the leaderboard must never
deserialise megabytes of samples, locally or remotely.

Writes go to a temp file and swap, so a crash mid-write cannot corrupt an existing file. An unreadable file is
renamed `.corrupt` rather than deleted — a corrupt file is evidence. Nothing here throws; a missing file is
simply a first run.

---

## How to test it

Ghost racing has no automated coverage — it needs a real run. From the editor:

1. **Enter play mode** on `Assets/Scenes/Level_01.unity`. `GhostRacing` installs itself; the console logs
   `No personal best yet`.
2. **Complete the level** — reach the boss and land the third deathblow. `GameEvents.BossDefeated` stops the
   timer, which ends the recording and submits it. You should see `NEW BEST mm:ss.ff`.
3. **Stop and re-enter play mode.** The console logs `Racing PB …` and a translucent ghost starts from the
   spawn when the timer starts. The delta appears under the timer once you are near the recorded path.

### Faster, without finishing the level

`F5` warps to the boss. Or drive it from `execute_code`:

```csharp
// status
VibeGame1.GhostRacing.I.Describe()

// save whatever has been recorded so far as a run, without beating the boss
VibeGame1.GhostRacing.I.CtxForceSave()

// race it
VibeGame1.GhostRacing.I.CtxLoadPersonalBest()

// inspect / reset
VibeGame1.GhostRacing.I.CtxLogBoard()
VibeGame1.GhostRacing.I.CtxClear()
```

All of these are also `[ContextMenu]` entries on the `GhostRacing` component in the scene — right-click the
component header in play mode. Nothing was added to the F1 test menu, because `TestMenu.cs` was out of scope
for this change.

**A useful smoke test:** `CtxForceSave()` after ~15 seconds of movement, then `CtxLoadPersonalBest()`. A ghost
should appear and retrace your path. That exercises record → store → load → playback → delta without a full run.

---

## Moving the leaderboard server-side

Everything is already callback-shaped for this, even though the local implementation answers immediately.
Implement `ILeaderboardBackend` against `UnityWebRequest` and call `Leaderboard.I.SetBackend(...)`. **No call
site changes.**

```csharp
public interface ILeaderboardBackend
{
    string Name { get; }
    void Submit(GhostRecording recording, Action<RunEntry> onDone);   // POST /runs
    void FetchTop(string level, int count, Action<RunEntry[]> onDone); // GET  /leaderboard
    void FetchGhost(string runId, Action<GhostRecording> onDone);      // GET  /runs/{id}/ghost
    void Clear(string level);
}
```

What a remote implementation must add, per the design doc's §7:

| Concern | Requirement |
|---|---|
| **Auth** | OIDC access token on every request. Never roll your own — §7.1. Bind tokens to the account. |
| **The client is never trusted** | The **server** decides the time. `RunEntry.verified` is server-set; the local backend must never set it true, and it does not. |
| **Anti-cheat** | Submissions carry the input stream too (§7.5 step 1) so the server can re-simulate the top N. Add an input recorder alongside `RunRecorder` — it does not replace it. |
| **Plausibility** | Reject times under a hand-set floor; reject position keyframes violating `FirstPersonMotor`'s `groundSpeed`/`dashSpeed`; reject impossible checkpoint ordering; flag ~100% perfect-parry rates (§7.5 step 2). |
| **Transport** | TLS 1.3 only. Hard payload cap (~2 MB) on ghost uploads. Per-IP and per-account rate limits, strictest on submission. |
| **Storage** | Index rows → Postgres. Blobs → S3-compatible (R2/B2, no egress fees). The local file split already matches this. |
| **Compression** | gzip blobs before upload — ~10× on this JSON. |

Accept up front that a determined cheater beats any client-side check. The goal is to make casual cheating
annoying and keep the top of the board verified (§7.5).

---

## Not implemented

Deliberately out of scope for Phase 1:

- **Any networking.** No NGO, no Relay, no Lobby, no co-op. Phase 2+ in the design doc.
- **The backend itself** (§6) — no HTTP service, no database, no object storage, no auth.
- **Input-stream recording** for server-side re-simulation (§7.5 step 1) — needed only once a server exists.
- **Server-side validation** (§7.5 step 2) — there is no server to validate.
- **Blob compression** — nothing to upload yet.
- **Phase 0 refactors** (§9): global `timeScale` decoupling, de-singletoning per-player state, seeded enemy
  RNG, `AttackSchedule`. **None of them are required for ghosts**, which is exactly why this phase ships first.
  They all remain prerequisites for co-op.
- **Ghost racing in the sandbox scene.** It bootstraps wherever a `SpeedrunTimer` exists, but the sandbox has
  no boss, so a run never *finishes* there — use `CtxForceSave()` if you want a sandbox ghost.

## Follow-up

`docs/DATAFLOW.md` needs a **Ghost racing** map added (CLAUDE.md hard rule: a system change updates its map in
the same change). That file was outside this change's permitted scope — it is the one outstanding item.
