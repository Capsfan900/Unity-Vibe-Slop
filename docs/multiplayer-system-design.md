# vibegame1 — Multiplayer & Backend System Design

**Status:** design only. No netcode exists in the project today.
**Audience:** whoever implements this.
**Date:** 2026-08-30 · **Target engine:** Unity 6000.5.10f1 (URP)

---

## 1. Executive summary, and the hard truth first

### The one fact that should drive every decision

**vibegame1's perfect-parry window is 130 ms.** That is not an incidental tuning value — it is the game. Everything downstream (posture, deathblows, Parry Juice, the ultimate) is gated on landing deflects inside that window.

130 ms is close to the worst possible number for netcode. Here is the arithmetic that matters:

| Latency source | Typical value |
|---|---|
| Client → server (upstream, half RTT) | 25–50 ms |
| Server → client (downstream, half RTT) | 25–50 ms |
| Interpolation buffer (2 ticks @ 30 Hz) | 66 ms |
| Client render/present | 8–16 ms |
| **Total client-view lag on an 80 ms RTT link** | **~110–130 ms** |

A naive server-authoritative implementation measures `elapsed = T_server(impact) − T_server(parry press received)`. That measurement includes the player's upstream latency *and* the interpolation buffer they were watching through. On an ordinary 80 ms connection those two together are **roughly one full parry window wide**. The result is not "slightly harder" — it is that a player who deflects perfectly, on their screen, gets told they blocked or got hit. The mechanic reads as broken, and no amount of tuning fixes it because the error scales with each player's ping.

Meanwhile a player on a 20 ms LAN connection would parry effortlessly. Latency would become the dominant skill input. That is unacceptable for this game.

### The three real options

| Option | How it works | Cost | Verdict for this game |
|---|---|---|---|
| **A. Client-authoritative parry + server validation** | The client owns the parry verdict. The server pre-announces each attack's impact tick; the client evaluates `ParryMath` locally against its own render clock and reports the result. Server validates plausibility, not timing. | **Low** — days, not weeks. Reuses `ParryMath` unchanged. | ✅ **Recommended.** |
| **B. Server-authoritative + lag compensation (rewind)** | Server keeps a ring buffer of per-client view state and rewinds to the client's render time to evaluate the deflect. Standard FPS hitscan lag comp, applied to parry timing. | **High** — 3–5 engineer-weeks, plus a permanent debugging tax. | Correct for competitive PvP. Overkill for co-op. |
| **C. Deterministic rollback (GGPO-style)** | Lockstep simulation with rollback and re-simulation on late input. | **Very high** — requires full determinism: fixed-point or bit-exact float math, deterministic physics, no `Random.Range`, no `Time.time` reads. This codebase is nowhere near it. | ❌ Not viable. Unity's `CharacterController` and NavMesh are not deterministic across machines. |

### Why option A is genuinely correct here, not just cheap

This is the key structural insight, and it is specific to how vibegame1 already works:

**The parry verdict depends only on data the client already holds locally and exactly.**

`ParryMath.Evaluate(elapsedSincePress, perfectWindow, lateWindow, facing, unblockable)` is a pure function (`Assets/Scripts/Combat/ParryMath.cs`). Its inputs are:

- `elapsedSincePress` — the gap between the player's own button press and the attack's impact.
- The attack's impact time — which is **scheduled in advance and telegraphed**. `EnemyController.BeginWindup` sets `stateEnd` and `cueTime`; `BeginStrike` sets `impactTime`. The game deliberately announces the impact ~0.28 s ahead via `cueLead`.

So the server can send *"attack `Boss_Slam` from enemy #7 will impact at server tick 4412"* well before it lands. The client converts that to its own local timeline, plays the cue, takes the press, and evaluates with **zero timing error**, because both the cue it reacted to and the press it made are local events on one clock.

The server then validates:

- Did that attack actually exist, and was this player inside its range/cone at impact?
- Was the player facing the attacker (`facingConeDeg`)?
- Was the claimed press within a plausible window given this client's measured RTT?
- Is the client's Perfect rate statistically sane over a session?

**What a cheater gains: auto-parry in a PvE co-op game.** They trivialise their own experience. They cannot steal loot from you, kill you, or corrupt your run. That is an acceptable trade for a mechanic that otherwise cannot feel right. If a competitive mode ever ships, that mode — and only that mode — moves to option B.

### Recommendation in one line

> Ship **asynchronous ghost racing + leaderboards first** (Section 2b). If you want live co-op, use **Netcode for GameObjects 2.x with a client-hosted listen server over Unity Relay**, server-authoritative for damage/health/enemies, **client-authoritative for the parry verdict with server validation**.

---

## 2. Scope options — pick the product before picking the tech

These are three genuinely different products. Do not let "add multiplayer" hide that choice.

### (a) 2–4 player co-op through the campaign

Friends run the level together, fight enemies and the boss together.

- **Effort: 10–16 engineer-weeks** for something that feels good.
- Requires: enemy authority + replication, player replication, the whole parry-latency problem, shared respawn/checkpoint rules, boss deathblow arbitration between multiple players, souls-split rules, item-pickup contention.
- Design questions with no obvious answer: does the boss's 3-segment deathblow require one player or any player? Do both players' postures matter? Does the run timer stop on the first or last player?
- **This is a real game redesign, not a feature.**

### (b) Asynchronous ghost racing + leaderboards ⭐ **recommended**

No live connection between players at all. You record the run, upload it, and race other people's ghosts.

- **Effort: 2–4 engineer-weeks total, including the backend.**
- **Zero latency problems.** The parry window is untouched. Combat code does not change at all.
- Fits the genre precisely. This is what Neon White actually does, and what makes speedrun games social: leaderboards, ghosts, and "you are 0.4 s behind Sam."
- The single-player game stays the product. Nothing regresses.
- Technically: record `InputReader` state + a periodic position keyframe at fixed tick; serialize, compress, upload. Replay drives a translucent ghost capsule. Position keyframes make replay robust even though the sim is not deterministic — the ghost is cosmetic, so drift is harmless.
- **Bonus: the recorded input stream is also your anti-cheat.** A leaderboard run ships its inputs; the server (or a moderator tool) can re-simulate and sanity-check it. You get verification for free from the same feature.

**Why this is the right first move:** it delivers the social hook ("join with friends" → *compete* with friends) at roughly 20% of the cost of co-op, carries none of the 130 ms risk, and every piece of backend you build for it (accounts, sessions, storage, leaderboards) is exactly the backend co-op would need later. It is not a detour; it is phase 1 of the same road.

### (c) Competitive versus

Players fight each other with parries.

- **Effort: 20+ engineer-weeks**, and it mandates option B lag compensation or full rollback.
- A 130 ms window in PvP with mismatched pings is a fairness nightmare — this is why fighting games invested a decade in rollback.
- ❌ **Do not attempt** at this stage.

---

## 3. Framework choice

Note: `com.unity.multiplayer.center` **1.0.1 is already in `Packages/manifest.json`** — that is Unity's *Multiplayer Center* recommendation window, not netcode. No actual networking library is installed.

| | **Netcode for GameObjects (NGO) 2.x** | **Fish-Net** | **Mirror** | **Photon Fusion 2** |
|---|---|---|---|---|
| Licence | Unity Companion Licence, source on [GitHub](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects) | Free tier fully functional; Pro is paid | [MIT](https://github.com/MirrorNetworking/Mirror) — no restrictions | Proprietary SDK |
| Cost | Free (transport free; UGS Relay metered) | Free; Pro **$10 one-time, then $2/mo** for updates | **Free forever** | Free 100 CCU (one app); **$125/mo** 500 CCU, **$250** 1k, **$500** 2k ([pricing](https://www.photonengine.com/fusion/pricing)) |
| Unity 6.5 support | ✅ **2.13.1 is released specifically for 6000.5** — exact version match | ✅ | ✅ Supports Unity 6 | ✅ |
| Relay / NAT traversal | Unity Relay (UGS) — first-party, well integrated | BYO (Steam, Epic, self-host, or third-party relay) | BYO | Photon Cloud included — best-in-class |
| Authority models | Client-server **and** distributed authority ([docs](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/distributed-authority.html)) | Client-server, client-side prediction built in | Client-server | Client-server + built-in prediction/rollback |
| CCU cap | None (you provide hosting) | **None** | None | Yes — pricing tiers |
| Prediction/reconciliation | Basic; you build it | ✅ Strong built-in prediction | Minimal | ✅ Strongest |
| Fit here | Best default | Best if you want prediction for free | Fine, most manual | Best tech, worst cost shape for a hobby project |

### Recommendation: **Netcode for GameObjects 2.x**

Reasoning, specific to this project:

1. **Version alignment is unusually clean.** NGO 2.13.1 targets 6000.5 — the exact pinned editor version. The project is already strict about version pinning; keep that discipline.
2. **First-party Relay integration** solves NAT traversal for "join with friends" with a join code, which is precisely the requested UX.
3. **No CCU cap and no per-seat cost** — Photon's model punishes success and adds a monthly bill to a prototype.
4. This game does **not** need heavy client prediction. Enemies are slow, telegraphed, melee-range. The player owns their own movement (see §4), so Fish-Net's main advantage over NGO is largely irrelevant here.
5. Mirror is a fine fallback if you ever want zero platform dependency, but you would then build lobby/relay yourself.

**Choose Fish-Net instead if** you later decide players' *movement* must be server-authoritative for anti-cheat — its prediction system would save real weeks.

---

## 4. Authority model and replication plan

**Topology:** client-hosted listen server (one player is host+client) over Unity Relay. A dedicated server is possible later without changing the code shape, if you use NGO's client-server topology rather than distributed authority.

> Prefer **client-server**, not distributed authority. Distributed authority spreads object ownership across clients, which is attractive for load but makes damage arbitration and anti-cheat significantly murkier. This game has one clear source of truth per enemy.

### 4.1 The replication table

| System | File | Authority | Notes |
|---|---|---|---|
| Player movement | `Player/FirstPersonMotor.cs` | **Client-owned**, server-validated | Client simulates and sends state. Server does plausibility checks (max speed vs `groundSpeed`/`dashSpeed`, teleport distance per tick, y within level bounds). Full server-authoritative movement with a `CharacterController` needs prediction/reconciliation — not worth it for co-op PvE. |
| Camera / look | `Player/PlayerLook.cs` | **Client-owned** | Yaw replicated for body orientation and the facing cone. Pitch is cosmetic. |
| **Parry verdict** | `Player/ParryController.cs`, `Combat/ParryMath.cs` | **Client-authoritative, server-validated** | See §1. `ParryMath.Evaluate` stays pure and unchanged — it just runs on the client. |
| Incoming attack resolution | `Player/PlayerCombat.ReceiveAttack` | **Split** | Must be decomposed. See §4.2 — this is the single biggest refactor. |
| Player health | `Combat/Health.cs` on the player | **Server** | Server applies damage; `Current`/`Max` replicated. Client predicts nothing here. |
| Player posture | `Player/PlayerPosture.cs` | **Server** | Derived from the parry verdict the client reported and the server accepted. Break is a server decision — it disables actions, so it must be authoritative. |
| Enemy FSM | `Enemies/EnemyController.cs` | **Server only** | Clients run a *visual* proxy: replicated position, yaw, state enum, and scheduled attack events. No client ever runs `DoImpact`. |
| Enemy NavMeshAgent | same | **Server only** | Never run `NavMeshAgent` on clients — disable the component on non-server instances or agents will fight the replicated transform. |
| Enemy health / posture | `Combat/Health.cs`, `Combat/Posture.cs` | **Server** | Replicated as quantized bytes for HUD bars. |
| Boss segments / phases | `Enemies/BossController.cs` | **Server** | Deathblow is a server-arbitrated claim (see §4.3). |
| **`TimeScaleController`** | `Core/TimeScaleController.cs` | ⛔ **LOCAL ONLY — never replicate** | See §4.4. This is the most dangerous file in the codebase for multiplayer. |
| Respawn / checkpoints | `Level/LevelManager.cs` | **Server** | Per-player checkpoints; enemy reset becomes a *shared* decision (do enemies reset when one player dies?). Design call required. |
| Souls | `Progression/SoulsWallet.cs` | **Server** | Currently a singleton with `static I`. Must become per-player and server-owned. Bloodstain drop-on-death likewise. |
| Item pickups | `Level/ItemPickup.cs` | **Server** | `OnTriggerEnter` currently runs client-side and calls `TryPickup` directly. Server must arbitrate — otherwise two players "get" the same item. Trigger detection stays client-side as a *request*; server confirms. |
| Speedrun timer | `Level/SpeedrunTimer.cs` | **Server** | One authoritative run clock. |
| All feel/FX | `Feel/*`, `UI/*` | **Local only** | `CameraShake`, `CameraFX`, `ScreenFlash`, `AudioManager`, `PlayerFeedback` must never be networked. Fire them from replicated *events*, on each client, for their own view. |

### 4.2 Decomposing `PlayerCombat.ReceiveAttack` — the core refactor

Today this one method does five different jobs in one call (`Assets/Scripts/Player/PlayerCombat.cs`):

```csharp
// current, abridged
public ParryResult ReceiveAttack(in AttackInfo a)
{
    var d = GameManager.I.statsData;                  // 1. global config lookup
    bool facing = Vector3.Angle(f, to) <= d.facingConeDeg;
    var result = parry.Resolve(a, facing);            // 2. timing verdict
    switch (result) {
        case ParryResult.Perfect:
            a.attacker.OnParried(...);                // 3. mutate the ATTACKER
            resources.AddJuice(...);                  // 4. mutate SELF
            TimeScaleController.I.HitStop(...);       // 5. LOCAL FX + global time
            CameraShake.I.Small();                    //    LOCAL FX
            ScreenFlash.I.Flash(...);                 //    LOCAL FX
            AudioManager.Play(Sfx.Parry, ...);        //    LOCAL FX
            break;
        ...
    }
}
```

Every one of those five needs a different authority. Split it into three:

```
1. ResolveParry(AttackInfo, facing) -> ParryResult        // CLIENT, pure, uses ParryMath
2. ApplyOutcome(AttackInfo, ParryResult) -> StateDelta    // SERVER, mutates Health/Posture/Juice/attacker
3. PlayFeedback(AttackInfo, ParryResult)                  // EVERY CLIENT, local FX only
```

Flow: client resolves (1) → sends `{attackId, result, clientTick}` → server validates and runs (2) → server broadcasts the outcome → all clients run (3) for their own view.

**Note that step 3 is nearly free**: the FX calls are already isolated behind singletons and `GameEvents`. The static event bus (`Core/GameEvents.cs`) is genuinely good architecture for this — UI and feel already subscribe rather than being called directly. Keep it, and make the server broadcast drive it.

### 4.3 Boss deathblow arbitration

`BossController` gives a 5 s deathblow window when HP hits 0. With multiple players, two can press execute in the same tick. **Server takes the first claim and rejects the rest**; the loser gets a "too slow" state rather than a desync. This must be an explicit server RPC, not a local `ExecuteInteractor` decision.

### 4.4 ⛔ `TimeScaleController` is incompatible with multiplayer as written

This deserves its own heading because it will silently ruin everything.

`Core/TimeScaleController.cs` writes **global `Time.timeScale`** and `Time.fixedDeltaTime`:

```csharp
Time.timeScale = world;
Time.fixedDeltaTime = baseFixedDelta * Mathf.Clamp(world, 0.05f, 1f);
```

It is driven by hitstop (`HitStop(0.09f)` on every parry) and the ultimate's slow-mo. In single player this is excellent design — it is the reason hits feel good. In multiplayer it is catastrophic:

- On the **host**, one player's parry hitstop would freeze the simulation for *everyone*, including all enemies and all other players.
- `EnemyController.Update` currently early-outs on `if (Time.timeScale <= 0f) return;` (line 89) and uses `float dt = Time.deltaTime;` (line 95) — so enemy AI is directly coupled to the global scale.
- The ultimate's 1.6 s slow-mo would make the host's session crawl for everyone.

**Required change:** the server simulation must run at a fixed, unmodified rate. Hitstop and slow-mo become **purely client-side presentation** — visual/audio only, never touching `Time.timeScale`, never affecting the authoritative sim.

The good news: the codebase is already halfway there. `PlayerDelta` exists precisely so the player is immune to hitstop, and the design intent ("the world freezes and the player does not") is documented. Extend that idea: *every* simulation reads an explicit delta; only rendering and FX read a scaled one.

---

## 5. Tick rate, snapshots, bandwidth

### Recommended parameters

| Parameter | Value | Why |
|---|---|---|
| Server tick rate | **30 Hz** | Enemies are slow and telegraphed. 60 Hz doubles cost for no perceptible gain in melee PvE. |
| Client send rate (input) | **60 Hz**, with the last 3 inputs redundantly packed | Input is tiny; redundancy removes the need for retransmission on loss. |
| Interpolation buffer | **2 ticks (≈66 ms)** | Standard. Tune to 3 ticks on poor links. |
| Extrapolation | ≤100 ms, players only | Never extrapolate enemy attack timing — that is exactly the data that must be exact. |
| Transport | Unreliable-sequenced for state, reliable for events | Attack schedules, damage, pickups, deaths = reliable. Positions = unreliable. |
| Delta vs snapshot | **Delta-compressed** against last-acked baseline | NGO `NetworkVariable` does dirty-flag deltas natively. |

### Quantization

| Field | Raw | Quantized | Bits |
|---|---|---|---|
| Position X | float | ±40 m @ 1 cm | 13 |
| Position Y | float | 0–40 m @ 1 cm | 12 |
| Position Z | float | 0–200 m @ 1 cm | 15 |
| Yaw | float | 0–360° @ 0.7° | 9 |
| Pitch (cosmetic) | float | ±90° @ 1.4° | 7 |
| Velocity (×3) | float | ±30 m/s @ 0.25 | 24 |
| State flags (grounded/dash/attack/parry/stagger/drink/execute/dead) | — | bitfield | 8 |
| Health / posture / juice | float | 0–255 normalized | 24 |
| Weapon + item count | int | — | 4 |
| **Total per player** | | | **116 bits ≈ 15 B** |

### Bandwidth budget — 4 players, 8 active enemies

**Per-player state:** 15 B payload + ~8 B NGO object/dirty overhead = **23 B/tick**
**Per-enemy state:** position 5 B + yaw 2 B + state 1 B + health 1 B + posture 1 B + attack id 1 B = 11 B + 7 B overhead = **18 B/tick** (enemies replicate at 15 Hz, half rate)

```
Downstream, per client:
  3 remote players × 23 B × 30 Hz  =  2,070 B/s
  8 enemies       × 18 B × 15 Hz  =  2,160 B/s
  events (attacks, damage, pickups) ≈  500 B/s
                                     ---------
                                       4,730 B/s  ≈  4.6 KB/s  ≈  38 kbit/s

Upstream, per client:
  input 9 B + 5 B overhead = 14 B × 60 Hz = 840 B/s
  × 3 (redundant last-3-inputs packing)   ≈ 2.0 KB/s  ≈ 16 kbit/s

Host upstream (listen server, 3 remote clients):
  3 × 4.6 KB/s ≈ 14 KB/s ≈ 113 kbit/s
```

**Verdict: trivial.** Any home broadband connection hosts this comfortably. Bandwidth is not a design constraint at 4 players; do not over-engineer compression before profiling.

### Relay cost check

All traffic transits Relay: `(4 × 4.6) + (4 × 2.0) ≈ 26 KB/s` per session ≈ **~94 MB per session-hour**.

Unity Relay's free tier is **3 GiB per CCU, capped at 150 GiB/month** combined across regions; overage is **$0.09/GiB (US+EU)** and **$0.16/GiB (Asia+AU)** ([UGS pricing](https://unity.com/products/gaming-services/pricing)).

> 150 GiB ÷ 94 MB ≈ **~1,600 session-hours per month free.** For a game played with friends, Relay is effectively free. Revisit only if you go wide.

### Interest management

At the current scale (one level, ~8 enemies, 4 players) **do not implement interest management.** It is complexity with no payoff. Add distance-based culling only if enemy counts exceed ~40.

---

## 6. Web backend

### What it actually needs to do

1. **Identity** — a stable player account.
2. **Lobbies** — create a session, get a 6-character join code, friends enter it.
3. **Relay allocation** — broker the Relay join code (co-op only).
4. **Leaderboards + ghost storage** — the ghost-racing product (§2b).
5. **Run validation** — reject impossible times.

### Recommended stack

| Layer | Choice | Why |
|---|---|---|
| Lobby + Relay | **Unity Lobby + Unity Relay (UGS)** | First-party, integrates with NGO in a few lines, free at this scale, and you do not write or operate NAT traversal. Do not build this yourself. |
| Identity | **Unity Authentication (anonymous → linked)** for co-op; **OIDC via an identity provider** (Auth0 / Clerk / Firebase Auth / Steam) if you add real accounts | Never roll your own auth. See §7. |
| Leaderboard/ghost API | **A small stateless HTTP service** — Go, or ASP.NET Core (C#, shares your language), or Node/Fastify | Only three endpoints: `POST /runs`, `GET /leaderboard`, `GET /runs/{id}/ghost`. |
| Ghost blob storage | **S3-compatible object storage** (Cloudflare R2, Backblaze B2) | Ghosts are ~50–300 KB compressed. R2 has no egress fees — meaningful when everyone downloads the WR ghost. |
| Metadata DB | **PostgreSQL** (Neon / Supabase / Fly Postgres free tier) | Leaderboard rows are tiny and relational. |
| Hosting | **Fly.io / Railway / Render**, single small instance | Autoscale-to-zero fits bursty hobby traffic. |

### Rough cost

| Item | Monthly |
|---|---|
| Unity Relay + Lobby | **$0** within free tier (~1,600 session-hrs) |
| API host (1 small instance) | **$0–7** |
| Postgres (free tier) | **$0** |
| R2/B2 object storage (few GB) | **$0–2** |
| **Total** | **~$0–10/month** |

Domain and TLS certificate via Let's Encrypt add ~$12/year.

### Relay vs direct connect

**Use Relay.** Direct connect requires port forwarding, which for "join with friends" means most sessions fail and you own the support burden. Relay costs ~$0 here and always works. Offer direct-IP as an advanced/LAN option only.

---

## 7. Security

### The golden rule

> **The client is never trusted.** Every client is an attacker's fully-owned machine running your code in a debugger.

Note this coexists with the §1 recommendation. Client-authoritative *parry* is a deliberate, bounded exception in a **co-op PvE** context where the only exploit is self-harm. It is not a general licence to trust clients. Damage numbers, health, souls, item grants, and leaderboard times are **never** client-decided.

### 7.1 Authentication

- **Use OIDC through an established identity provider.** Do not implement password storage. If you ever do handle passwords, Argon2id — but the correct move is not to.
- **Never store plaintext credentials**, in the DB, in logs, or in error payloads.
- Short-lived **access tokens (15 min)**, long-lived **refresh tokens (30 days) with rotation and reuse detection**.
- Bind session tokens to the lobby; a token for lobby A must not act on lobby B.
- Anonymous-first is fine for a prototype (Unity Auth), with account linking later.

### 7.2 Transport

- **TLS 1.3** on every HTTP endpoint. HTTP → HTTPS redirect, HSTS with `includeSubDomains`.
- **WSS only** for websockets. Never `ws://`.
- Game traffic over Relay is DTLS-encrypted by Unity — do not hand-roll crypto for the UDP path.
- Modern cipher suites only; disable TLS 1.0/1.1.

### 7.3 Input validation

- **Schema-validate every request at the boundary** and reject unknown fields. Never deserialize straight into a domain object.
- Enforce **payload size limits** — a hard cap on ghost uploads (e.g. 2 MB) and on every RPC.
- Parameterized SQL only. No string-built queries.
- Validate every `ServerRpc` argument: the caller's `senderClientId` **must** be re-derived server-side, never taken from the payload. `[ServerRpc(RequireOwnership = true)]` by default.
- Reject client-sent damage, health, position deltas, souls, or item grants outright — those are server-computed values, and a client sending them is a bug or an attack.

### 7.4 Rate limiting and DDoS

- Per-IP **and** per-account rate limits on all HTTP endpoints; strictest on run submission (e.g. 10/min).
- Put the API behind **Cloudflare** (free tier) for L3/L4 absorption and bot filtering.
- Per-connection RPC rate limits in-game; disconnect clients that exceed them.
- Cap lobby creation per account per hour.

### 7.5 Leaderboard integrity — the part that actually matters

For a speedrun game the leaderboard *is* the multiplayer. Protect it accordingly:

1. **Submit the input stream, not just the time.** The client uploads the recorded `InputReader` sequence plus periodic position keyframes.
2. **Server-side plausibility checks** (cheap, run on every submission):
   - Time below a hand-set theoretical floor per level → reject.
   - Position keyframes violating `groundSpeed`/`dashSpeed` from `FirstPersonMotor` → reject.
   - Impossible checkpoint ordering or missing checkpoints → reject.
   - Perfect-parry rate at ~100% across a long run → flag for review.
3. **Re-simulation for the top N.** Verify podium runs by replaying inputs headlessly. This does **not** need bit-exact determinism — you are checking that the inputs plausibly produce the claimed route and time, not reproducing it exactly.
4. **Sign submissions** with the session token; reject replays via a nonce + timestamp window.
5. **Separate verified and unverified boards.** Fastest honest path: show everything, mark verified runs, and let moderators promote.

Accept up front that a determined cheater beats any client-side check. The goal is to make casual cheating annoying and to keep the top of the board verified.

### 7.6 Secrets and supply chain

- **No secrets in the repo, ever.** The existing `.gitignore` covers Unity build output but is not a secrets strategy. Use environment variables and the host's secret manager (Fly secrets, Railway variables).
- Add a pre-commit secret scanner (`gitleaks`) — cheap insurance.
- Pin dependency versions; enable Dependabot/Renovate.
- Never ship server secrets inside the Unity client — **anything in the build is public**, including strings in `ScriptableObject`s.
- Sign release builds.

### 7.7 OWASP-aligned checklist

| Risk | Mitigation |
|---|---|
| Broken access control | Server re-derives identity from token; ownership checks on every RPC |
| Cryptographic failures | TLS 1.3 / DTLS; no custom crypto; no secrets client-side |
| Injection | Parameterized queries; schema validation; reject unknown fields |
| Insecure design | Server-authoritative by default; parry exception scoped to co-op PvE and documented |
| Security misconfiguration | Least-privilege DB user; no stack traces to clients; CORS locked to known origins |
| Vulnerable components | Pinned deps + automated scanning |
| Auth failures | OIDC provider; short tokens; rotation with reuse detection |
| Integrity failures | Signed builds; signed run submissions |
| Logging failures | Structured logs, no PII/tokens; alerts on auth-failure and rate-limit spikes |
| SSRF | No user-supplied URLs fetched server-side |

---

## 8. Performance

### Server tick budget (30 Hz → 33.3 ms/tick)

| Work | Budget |
|---|---|
| Enemy FSM + NavMesh (8 enemies) | 3 ms |
| Physics queries (combat) | 2 ms |
| Player movement validation (4) | 1 ms |
| Serialization + send | 3 ms |
| **Headroom** | **~24 ms** |

Comfortable. The listen-server host also renders, so budget the host at ~50% of a frame.

### Why the existing combat queries are already well-suited to server authority

`WeaponController` and the item effects use `Physics.OverlapSphereNonAlloc(...)` against `Layers.EnemyMask` with a **pre-allocated buffer**. That is exactly the right shape for a server tick:

- Non-allocating → no GC pressure in the hot path.
- Layer-masked → the query cost scales with enemies, not scene complexity.
- Stateless and instantaneous → trivially re-runnable server-side with no client input beyond "I attacked at tick N."

This is a genuine piece of luck: the melee combat model (short-range, instantaneous, sphere-query) is *far* easier to make server-authoritative than projectile or hitscan combat would be. Melee's usual netcode downside is the tight timing — which §1 addresses separately.

### GC discipline

- Keep the `NonAlloc` pattern everywhere; never `Physics.OverlapSphere` (allocating) on the server.
- `PlayerItems.DoLightning` allocates a `HashSet` and `List` per activation — fine at a few uses per run, but hoist them to fields if item usage ever becomes frequent.
- `GameEvents` closures: `BossController.Init` subscribes lambdas capturing `this`; ensure symmetric unsubscribe on despawn or you leak across level resets.
- Avoid per-tick `string` formatting in networked code (the HUD readout is client-only — keep it that way).

### What to profile

1. **Server tick time** with 4 players + boss (worst case).
2. **NavMeshAgent cost** — the most likely server hotspot; consider staggering `SetDestination` across ticks rather than every enemy every tick (`EnemyController.cs:107` currently sets it every frame in Chase).
3. **Bandwidth per client** against the §5 budget.
4. **Time-to-first-frame on join** — late-join state sync is often the worst UX bug.
5. **GC allocations per tick** — target zero in the server loop.

---

## 9. Roadmap

### Phase 0 — prerequisite refactors (**3–4 engineer-weeks**) — *do this before any netcode*

None of this requires a networking library. All of it makes the single-player game better-structured, and all of it is mandatory before netcode is tractable.

| # | Refactor | Why |
|---|---|---|
| 1 | **Break the global `Time.timeScale` dependence.** Hitstop and slow-mo become client-side presentation. Simulation reads an explicit delta. | §4.4 — otherwise one player's parry freezes everyone. |
| 2 | **Remove single-player assumptions from enemies.** `EnemyController.cs:59` does `FindAnyObjectByType<PlayerCombat>()` — one global player. Enemies need a target *set* and target selection. | Hard blocker for co-op. |
| 3 | **Make combat resolution take explicit actors.** Split `PlayerCombat.ReceiveAttack` per §4.2; stop reaching through `GameManager.I`, `TimeScaleController.I`, `CameraShake.I` inside the damage path. | Enables server-side execution with no scene singletons. |
| 4 | **Deterministic, seeded RNG for enemy decisions.** `EnemyController.ChooseCombo` (line 154) uses `Random.Range`. Replace with a seeded per-enemy stream so the server can drive it and clients can predict telegraphs. | Attack choice must be authoritative and reproducible. |
| 5 | **De-singleton per-player state.** `SoulsWallet` (`static I`), `PlayerResources`, `SpeedrunTimer` are per-player concepts implemented as globals. | 10 singletons exist today; roughly half are per-player. |
| 6 | **Schedule attacks explicitly.** Formalize `cueTime`/`impactTime` into a serializable `AttackSchedule {attackId, enemyId, impactTick}`. | This is the payload that makes client-side parry work. |
| 7 | **Extract the input recorder.** `InputReader` gains record/replay. | Prerequisite for ghosts *and* for replay-based run validation. |

> **Phase 0 has standalone value.** Even if multiplayer is cancelled, items 1, 3, 5 and 7 leave a materially better codebase — and item 7 alone unlocks the ghost feature.

### Phase 1 — Ghost racing + leaderboards (**2–3 engineer-weeks**) ⭐ ship this first

- Record + serialize + compress runs (builds on Phase 0 item 7).
- Backend: identity, `POST /runs`, `GET /leaderboard`, ghost blob storage.
- In-game: leaderboard UI, ghost download, translucent ghost playback.
- Server-side plausibility validation (§7.5 steps 1–2).
- **Shippable, social, zero latency risk.**

### Phase 2 — Co-op foundation (**4–6 engineer-weeks**)

- Install NGO 2.13.1; Unity Lobby + Relay; join-code flow.
- Player replication (movement, look, animation state).
- Server-authoritative enemies with replicated visual proxies.
- The client-authoritative parry protocol + server validation (§1 option A).
- **Milestone: 2 players, one level, enemies work, parry feels correct on a 100 ms link.** Do not proceed until parry feel is verified with artificial latency.

### Phase 3 — Co-op completeness (**4–6 engineer-weeks**)

- Boss deathblow arbitration; per-player checkpoints and respawn rules.
- Souls split, item contention, bloodstains.
- Late join, host migration or graceful host-quit, disconnect handling.
- Co-op balance pass (enemy count/HP scaling).

### Phase 4 — Hardening (**2–3 engineer-weeks**)

- Full security checklist (§7); rate limits; monitoring and alerting.
- Network simulation testing (latency, jitter, packet loss).
- Load test the backend.

**Total to co-op: ~15–22 engineer-weeks. Total to shipped ghost racing: ~5–7.**

---

## 10. Risks and non-goals

### Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Parry does not feel right over the network** | 🔴 Critical | Prototype the parry protocol under simulated 100/150/200 ms latency **in Phase 2, first**. If it fails there, stop — do not build Phase 3 on top of it. |
| Global `timeScale` refactor destabilises game feel | 🟠 High | Phase 0 item 1 is invasive and touches the best-feeling part of the game. Do it against the existing EditMode tests and the `DebugHarness` scenarios, single-player, before any netcode. |
| Co-op is a design problem, not an engineering one | 🟠 High | Boss deathblow, run timer, souls split and respawn rules have no default answer. Decide them on paper before implementing. |
| Scope: co-op quietly becomes the whole project | 🟠 High | This is 15–22 weeks against a prototype whose stated deliverable is "mechanics and feel." Phase 1 exists precisely to deliver the social hook without that bet. |
| Host advantage (0 ms) vs clients | 🟡 Medium | Inherent to listen servers. Acceptable in PvE. Would require a dedicated server for PvP. |
| NavMesh cost with more enemies | 🟡 Medium | Stagger `SetDestination`; profile early. |
| Relay free tier exceeded | 🟢 Low | ~1,600 session-hours/month. Monitor; overage is cents. |

### Explicit non-goals

- ❌ **Competitive PvP.** Requires rollback or full lag compensation. Out of scope.
- ❌ **Dedicated server hosting.** Listen server + Relay is correct at this scale.
- ❌ **Deterministic lockstep.** Unity's `CharacterController` and NavMesh are not cross-machine deterministic.
- ❌ **Anti-cheat beyond server authority + plausibility.** No kernel anti-cheat, no obfuscation theatre.
- ❌ **Cross-play beyond Windows/WebGL.** Only those two build modules are installed. Note WebGL cannot use UDP — it needs WebSockets, which adds latency and a separate transport path. **If WebGL co-op matters, decide that now**, because it changes the transport choice.
- ❌ **Player count above 4.** Every system here is sized for 2–4.

---

## Appendix A — decision summary

| Decision | Choice |
|---|---|
| Ship first | Async ghost racing + leaderboards |
| Netcode library | Netcode for GameObjects 2.13.1 |
| Topology | Client-hosted listen server, client-server authority |
| Transport / NAT | Unity Relay + Unity Lobby (join codes) |
| Parry authority | **Client-authoritative, server-validated** |
| Damage / health / souls / items | Server-authoritative, always |
| `TimeScaleController` | Local presentation only — never networked |
| Tick rate | 30 Hz server, 60 Hz input, 2-tick interpolation |
| Backend | Small stateless HTTP service + Postgres + S3-compatible storage |
| Auth | OIDC via a provider — never roll your own |
| Estimated backend cost | ~$0–10/month at this scale |

## Appendix B — sources

- [Netcode for GameObjects — Unity 6 manual](https://docs.unity3d.com/6000.5/Documentation/Manual/com.unity.netcode.gameobjects.html)
- [NGO GitHub (version/licence)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects)
- [NGO — Authority and distributed authority topologies](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/distributed-authority.html)
- [Unity Gaming Services pricing (Relay/Lobby free tiers)](https://unity.com/products/gaming-services/pricing)
- [Relay vs Lobby](https://docs.unity.com/en-us/relay/relay-vs-lobby)
- [Photon Fusion pricing](https://www.photonengine.com/fusion/pricing)
- [Fish-Net — Pro, projects and support](https://fish-networking.gitbook.io/docs/overview/readme/pro-projects-and-support)
- [Mirror Networking (MIT)](https://github.com/MirrorNetworking/Mirror)
- [Rollback vs delay-based netcode background](https://news.ycombinator.com/item?id=34399790)
