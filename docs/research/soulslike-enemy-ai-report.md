# Building Soulslike Combat Enemy AI in Unity: Design Principles from FromSoftware & Lies of P, and a Technical Implementation Blueprint

*External research report supplied by the user, 2026-09-06. Community-sourced; see Caveats at the end.*

## TL;DR
- **FromSoftware's acclaimed enemy AI is technically "low-tech": a stack-based state system (a pushdown automaton) written in Havok Script/Lua that performs weighted-random attack selection modified by player distance, cooldowns, HP thresholds, and event "interrupts" — NOT behavior trees, GOAP, or machine learning. The "intelligence" lives in animation-driven attack data and per-boss tuning, not in a fancy planner.** You can replicate the *feel* in Unity with a small hierarchical/stack FSM plus data-driven ScriptableObject attack definitions.
- **The three pillars that make the combat "feel good" are all designer-facing, not AI-algorithmic: (1) clearly telegraphed, deliberately-timed attacks with punishable recovery windows; (2) a poise/posture/stagger economy that governs who can act; and (3) attack selection that reads player distance/state so it feels intentional rather than random.** Lies of P evolves this with a Bloodborne-style "Guard Regain" rally and a tight (~155 ms) Perfect Guard parry that enemy attacks are explicitly designed around.
- **In Unity, build it as: a stack/hierarchical FSM "brain" → ScriptableObject `AttackDefinition` assets (damage, poise damage, range, windup/active/recovery frames, cooldown, weight) → Animancer or Animator with Animation Events driving hitbox activation → a shared `PoiseComponent`/`StaggerComponent` → NavMeshAgent (or A* Pathfinding Project) for repositioning.** Weighted selection + cooldown/history tracking is the single most important trick for intentional-feeling AI.

## Key Findings

**1. FromSoft's real AI architecture is a pushdown automaton of "Goals," not behavior trees.** Reverse-engineering of the decompiled Havok Script/Lua in Dark Souls, Bloodborne, Sekiro and Elden Ring shows each enemy ("Actor") runs a *stack* of "Goals" (their term for a state). Each frame the top Goal updates; it can push sub-Goals (e.g., `Attack(R1, Combo)`), and each returns Continue/Success/Failure. This is more flexible than a plain FSM but far cheaper than behavior trees or planners. Attack choice happens in an `Activate` callback that does **weighted random selection** among action functions, with weights modified by target distance, RNG, HP thresholds and per-move cooldowns. An `Interrupt` callback lets external events (taking damage, the player casting a spell or drinking an Estus, entering a spatial "watch region" behind the boss) immediately abort the current action and launch a response.

**2. Attacks are animation-driven; the animation carries the gameplay data.** A Goal simply says "play this attack animation," and **animation events** embedded in the clip carry hitbox activation/deactivation, timing, projectile spawns and special effects. "Combo" features just switch which set of events is active to allow faster chaining. This is the single most important architectural takeaway for Unity: the attack *is* the animation plus its event track.

**3. Telegraphing and deliberate timing are what make it fair.** Good enemy attacks have a readable windup (the "tell"), a short active window, and a recovery window that is the player's punish opportunity. FromSoft deliberately uses "delayed" attacks (e.g., Margit's charged cane slam) to punish panic-rolling, and later Elden Ring bosses push this further with long, variable windups that demand you learn the animation.

**4. Poise/posture/stagger is a second health economy that governs who gets to act.** In Sekiro, both player and enemies have a **Posture** bar; deflecting deals heavy posture damage to the attacker; when posture breaks the target is staggered and open to a Deathblow. Per FromSoftware's official Sekiro web manual, "An enemy's Vitality is tied to how fast his Posture recovers – the lower the Vitality, the slower the Posture recovery," and the manual recommends "first focus on reducing their Vitality then focus on breaking their Posture" — which is why you chip HP first, then break posture. In Elden Ring, **poise** is a hidden "poise HP" bar: each attack deals poise damage; at zero the target is staggered and poise resets to max. Player poise doesn't regen over time but fully resets 30 seconds after the last hit; NPC "stance" decays if you don't build it fast enough.

**5. Boss AI: multi-phase, distance-aware, weighted, non-random.** Bosses switch move pools by range (close = melee combo strings; far = gap-closers, ranged/AOE), scale aggression at HP thresholds, and transition to new phases (often with cinematic + full or new moveset). The community-observed behavior — e.g., a boss allocating more close-range attacks after crossing a HP threshold — maps exactly to the decompiled pattern of HP-thresholded weight modification. Crucially, FromSoft avoids pure randomness: weights are gated by distance, cooldowns prevent move spam, and combo chains are authored sequences.

**6. Lies of P evolved the formula around a proactive guard.** In his post-launch Game Rant interview, director Jiwon Choi said: "In Lies of P, we wanted to offer unique combat experiences to players... We wanted the guard to be a proactive option for players, allowing them to use it to overwhelm their enemies or weaken them. Guard regain also follows this idea of providing a more proactive combat experience." Its **Perfect Guard** is a Sekiro-style deflect but tighter (~155 ms window vs Sekiro's ~200 ms), negates chip damage, builds enemy stagger, and can break enemy weapons. Normal blocking feeds **Guard Regain** (a Bloodborne-style rally: blocked damage becomes recoverable HP you win back by attacking). Enemies are explicitly designed with big windups and fast actual strikes so that Perfect Guard timing keys off the weapon, not the body.

## Details

### PART 1 — Design principles distilled

#### Core soulslike combat philosophy
The moment-to-moment loop is an **attrition-free, avoidance-based** exchange: enemies "ask questions" (can you dodge/block/parry this?) and the player answers by reading telegraphs. The four ingredients:

- **Telegraphed attacks with deliberate timing.** Every attack has a windup animation (the tell), often reinforced with audio and VFX. The tell must give enough reaction time to be fair; a tell that gives no time to react is considered a design failure. FromSoft deliberately varies windup length (delayed slams) to punish rote panic-rolling, forcing players to actually read the animation rather than dodge on a fixed rhythm.
- **Punishable openings / recovery windows.** After an attack's active frames, the enemy is in recovery — this is the player's invited punish window. The size of that window is the difficulty knob.
- **Risk/reward spacing and stamina interplay.** In Dark Souls, stamina gates attacking, rolling, blocking and sprinting; light attacks are low-commitment/low-cost, heavy attacks are high-damage but leave you vulnerable. Running out of stamina ("bottoming out") is lethal, and blocking an attack that empties stamina breaks your guard. This makes spacing (staying at the edge of the enemy's range) the core skill. The roll's invincibility frames (i-frames) are a stamina-gated resource.
- **Aggression patterns per archetype.** Fodder enemies use small, simple move pools and exist to be setups/ambushes or to be fought in groups; elites have larger pools and more conditionals; bosses have the largest pools, multi-phase logic, distance-gated move sets, and per-move cooldowns.

#### Poise / posture / stagger and how they feed AI decisions
- **Sekiro Posture:** A gauge on both combatants. Landing hits (even blocked) raises the target's posture; **deflecting** deals large posture damage to the attacker but little to the defender. Break the enemy's posture → Deathblow. Key AI-relevant detail: an enemy's posture recovers faster at high HP and much slower at low HP (tied to Vitality, per the official manual; community testing shows recovery effectively stalls around 50% HP), so the intended strategy is chip HP → then posture-break. Attacks cannot regain posture; per the Sekiro Fextralife Wiki, "If Posture is close to breaking, the bar will flash orange. If the posture bar reaches max capacity, the player or enemy becomes vulnerable."
- **Elden Ring poise/stance:** Hidden "poise HP" per character. Every attack has a specific **poise damage** value; when poise reaches zero the target staggers and poise resets to full (leftover damage does not carry over). Player poise comes from armor and functions as "hyper armor" during attack animation startup/active frames — letting heavy-weapon users trade through hits. NPCs have a separate "stance" that decays over time if not pressured quickly.
- **AI hook:** The important design move is that stagger is a *state* the AI transitions into (staggered/vulnerable), and conversely enemies can be scripted to become *more* aggressive at HP or player-state thresholds. In the decompiled Elden Ring logic, taking damage or a player action fires an interrupt that can immediately change behavior.

#### Boss AI specifics
- **Weighted, distance-gated selection.** The decompiled pattern: compute `target_distance`; pick a weight table for "far / mid / close" bands; zero-out moves on cooldown; then do weighted random among action functions. Far band favors gap-closers and ranged/AOE; close band favors combo strings.
- **Combo chaining rules.** Combos are authored as pushed sub-Goals (e.g., R1-initial → R1-repeat → optional extra → R1-finisher, with the extra gated by an RNG roll). This yields variety without pure randomness.
- **Cooldowns / history.** Each move checks "last played" data on the Actor; recently-used heavy moves have their weight set to zero for a few seconds, preventing spam.
- **Interrupts = "evil" reactive features.** Per the nega.tv reverse-engineering write-up, "the Bell Bearing Hunter will detect you spell casting or using an item and from there has an 85% chance to immediately abort its current action and launch into an attack. They also make use of dynamic spatial watch regions configured on Actors, which trigger interrupts" — for example a watch region behind or under a boss to punish players trying to get clever. This is how From makes bosses feel like they're "reading" you without any learning system.
- **Multi-phase design.** Phase transitions (Ludwig, Sister Friede's three phases, Godfrey→Hoarah Loux, Nameless King, Malenia's ascension) typically add moves, raise aggression, and often heal/reset with a cinematic that doubles as a breather. Community observation that Malenia shifts toward close-range attacks past a HP threshold matches the HP-thresholded weight modification in the code.

#### Common enemy archetypes and how their state logic differs
- **Melee-aggressive (fodder/elite):** small Goal set — Approach → Attack combo → Reposition/Circle → Backstep. Short cooldowns, close-band weights only.
- **Ranged/caster:** wants to *maintain* distance — kite logic (retreat when player closes, cast when at range), distinct "far-band" heavy weighting.
- **Ambush:** passive Goal until a trigger (level designers can set a passive top-level Goal per placed enemy), then aggro.
- **Group/pack:** aggro propagation and spacing so they don't all attack simultaneously — often a "token" system where only N attackers may commit at once (see Unity implementation below).

#### Lies of P (Round8/Neowiz) — how it adapted the formula
- **Perfect Guard** is the centrepiece: a deflect timed to the incoming strike. It negates chip damage, costs only stamina, builds the enemy's (hidden) stagger, protects against unblockable red "Fury" attacks (which dodge i-frames don't avoid), and progressively **breaks enemy weapons** (reducing their damage and reach). The window is community-measured at ~155 ms (roughly 9–9.3 frames at 60 fps); a Nexus mod author notes the window duration is static in Lies of P (unlike Sekiro, which shrinks its window on repeated spam), and one mod extends the 155 ms window to 310 ms for accessibility. Mashing the guard button triggers a ~30-frame lockout.
- **Guard Regain (rally):** a normal (non-perfect) block converts blocked damage into a recoverable chunk of the health bar (shown as a darker/red segment) that you win back by attacking, and lose over time if you don't. This is explicitly Bloodborne's Rally re-tied to blocking, and it pushes players to stay aggressive after defending. (Note: damage from unblocked red "Fury" attacks does not generate Guard Regain.)
- **Stagger/"Groggy" → Fatal Attack:** Repeated hits, charge attacks, Fable Arts, and Perfect Guards fill a hidden stagger pool. When it fills, the enemy's HP bar flashes white (the "staggerable" window). Landing a **charged heavy attack, Fable Art, or Perfect Guard while the bar is white** actually breaks stance and opens a **Fatal Attack** (the game's visceral/critical riposte, indicated by a glowing red circle on the ground). The staggerable window is timed (~5 s base per data-mined values) and can be extended via the P-Organ tree; this is Lies of P's analogue to Sekiro posture, though the gauge is never shown numerically. The key nuance: turning the bar white ≠ staggered — a charged heavy (or Fable Art / Perfect Guard) must land on the white bar to actually break stance.
- **P-Organ tree & Fable Arts** provide combat buffs directly tied to the guard loop (e.g., "Perfect Block Stiffen," Perfect-Guard weapon-durability recovery, Fable charge on Perfect Guard, extended staggerable/staggered windows). Fable Arts are weapon skills fueled by attacking, split into offensive Blade arts and defensive Handle arts.
- **Design intent (Choi):** the team "explored various options across all aspects of combat design, not just in attack but dodge and guard," wanting the guard to let players "overwhelm their enemies or weaken them." Enemy attacks were built with big, baiting windups and quick actual strikes so that timing keys off the weapon contact. Note: I found no dedicated GDC *talk* dissecting the enemy-AI architecture; the on-record design statements come from press/post-launch interviews (Game Rant, Gamescom, the Overture GDC press interviews).

#### Player-facing feedback loops
- **Hit reactions** are driven by the same poise/stagger economy (flinch vs. hyper-armor trade vs. full stagger animation).
- **Lock-on** reframes movement (strafe/orbit) and camera, and enemy attack selection can read whether the player is locked on, blocking, low on stamina, back-turned, casting, or drinking — via interrupts and state checks.
- **Camera** behavior (especially with large enemies) is a known pain point; readable tells partly depend on keeping the attack on-screen.

### PART 2 — Unity technical implementation blueprint

#### A. AI decision-making architecture — recommendation
**Use a hierarchical/stack FSM as the "brain," not a monolithic behavior tree.** The Elden Ring decompilation is a strong argument that a stack-of-states with imperative weighted selection is both cheaper and more legible than BTs/planners for scripted-feeling boss combat. Concretely:

- **`EnemyBrain` (MonoBehaviour)** — owns the state stack, references the blackboard, the `AnimancerComponent`/`Animator`, the `NavMeshAgent`, `PoiseComponent`, and the `AttackLibrary`.
- **State classes** (`IState` or abstract `EnemyState`): `IdleState`, `ApproachState`, `StrafeState`, `AttackState`, `RetreatState`, `StaggeredState`, `PhaseTransitionState`. Model as a small stack so an `AttackState` can push a `RepositionState` then resume.
- **`CombatDecisionMaker`** — the `Activate`-equivalent. Each time the enemy finishes an action it: reads distance band + player state from the blackboard, builds a weight array over candidate `AttackDefinition`s, zeroes cooldowns/recent moves, and does weighted-random selection. This is where "intentional not random" is engineered.
- **`InterruptHandler`** — subscribes to events (took damage, player-cast-detected, player-healing, player-behind) and can clear the current state and push a reactive one, mirroring FromSoft interrupts.

**When to reach for third-party tools:** If your team includes non-programmer designers who want to author logic visually, **NodeCanvas** (BT + FSM + dialogue, and it lets you nest a BT inside an FSM state — ideal for "one FSM state per boss phase, BT inside") or **Behavior Designer** (Opsive, integrates cleanly with A* Pathfinding Project and has broad third-party action support) are the two standard Asset Store choices. Pros: visual authoring, reuse, debugging. Cons: overhead, and naive BT re-evaluation can be slower than a hand-rolled stack FSM — for a boss with few states this is unnecessary weight. **Utility AI** is worth layering *only* for the scoring step (it's essentially your weighted selection). **GOAP is not recommended** for scripted boss combat — it moves authored choreography out of designers' hands and adds a search cost for no benefit here.

#### B. Animation-driven combat & hit detection
- **Animator vs Animancer:** Unity's **Animator/Mecanim** works but forces you into a state-graph with string parameters. **Animancer** (Pro is $90 on the Kybernetik store, latest v8.2.3 as of October 2025; **Animancer Lite is free** and unlocks most features in the editor) lets you `animancer.Play(clip)` on demand, attach End events, organize clips in ScriptableObjects, and avoid "magic strings" — a much better fit for data-driven attack systems where each `AttackDefinition` references an `AnimationClip` directly. Recommendation: Animancer for combat characters; keep Mecanim blend trees only if you prefer them for locomotion.
- **Root motion vs script-driven:** Use **root motion for attacks** (lunges, steps) so hit spacing matches the animation authoring; use **script/agent-driven movement for locomotion** (chase/strafe) so pathfinding stays in control. Blend by disabling `applyRootMotion` outside attack states.
- **Locomotion blend trees:** 2D directional blend tree (forward/strafe/back) driven by the agent's desired velocity relative to facing, so lock-on strafing reads correctly.
- **Hitboxes/hurtboxes:** Three viable approaches:
  1. **Animation-event-toggled trigger colliders** (simplest, most common): child GameObject with a trigger collider on a `Hitbox` layer; `AnimationEvent` calls `EnableHitbox()`/`DisableHitbox()` at the exact active-frame boundaries. Use a Layer Collision Matrix so `Hitbox` only collides with `Hurtbox`.
  2. **Weapon-attached collider** animated to follow the blade.
  3. **Capsule-cast "sword trail"** between the weapon's base and tip each FixedUpdate while active — best for fast weapons where a static collider would tunnel past a thin target.
  Track already-hit targets per swing (a `HashSet`) so one swing hits each target once. Do damage from the hit event, not in `OnTriggerStay` (which fires every physics frame).
- **Frame data (startup/active/recovery) like a fighting game:** author these as fields on the `AttackDefinition` (in frames at your animation authoring rate — FromSoft authors at a 30fps reference) and place the actual hitbox enable/disable via Animation Events so data and animation never desync. Recovery = the punish window; expose it so designers can tune difficulty.

#### C. Poise / posture / stagger in Unity
- **`PoiseComponent`**: `float maxPoise; float currentPoise; float regenDelay; float regenRate; bool hasHyperArmor;`
- Each hit calls `ApplyPoiseDamage(float poiseDamage)`. If `currentPoise <= 0` → fire `OnStaggered` (brain pushes `StaggeredState`, plays stagger anim, opens the critical/riposte window) and reset `currentPoise = maxPoise`.
- **Regen model:** Sekiro-style — start regen after `regenDelay` since last poise hit; scale `regenRate` down as HP drops (so low-HP enemies stagger-lock more easily). Elden-Ring-style alternative — decay the *accumulated stance* if not pressured within a window.
- **Hyper armor:** during an attack's active frames, set a flag so incoming hits deal poise damage but don't interrupt unless they exceed a threshold.
- **AI hook:** `OnStaggered` transitions the enemy to a vulnerable state; `OnHealthThreshold` (e.g., 50%) raises an aggression multiplier applied to the weight tables (mirroring HP-thresholded weight changes in Elden Ring).

#### D. Data-driven attack design with ScriptableObjects
Define attacks as assets so designers tune AI without code:
```csharp
[CreateAssetMenu(menuName="Combat/Attack Definition")]
public class AttackDefinition : ScriptableObject {
    public AnimationClip clip;
    public float damage;
    public float poiseDamage;
    public float staminaCost;
    public float minRange, maxRange;   // distance band this move is valid in
    public int startupFrames, activeFrames, recoveryFrames;
    public float cooldown;             // per-move cooldown
    public float baseWeight;           // for weighted random selection
    public AttackDefinition[] comboFollowups; // chain rules
    public bool isUnblockable;         // red "perilous"/Fury attack
    public string tellVfxId;           // telegraph cue
}
```
Group them in an `AttackLibrary`/`MovesetSO` per enemy (and per phase). This is the Type Object / Strategy pattern with ScriptableObjects — the standard Unity data-driven approach. Keep *runtime* mutable state (cooldown timers, last-used) on a separate runtime wrapper, not on the shared SO asset, to avoid cross-instance bleed.

#### E. Movement / positioning
- **NavMeshAgent** handles most needs. For "maintain distance" (casters), set `stoppingDistance` and steer to a point on a ring around the player rather than the player itself. For **circle-strafing**, feed the agent a target position offset tangentially around the player and update it at a throttled rate (not every frame — that tanks performance with many agents).
- For enemies with distinct movement styles (shield-pivot, kiting), compute a desired velocity and set `agent.velocity` or a series of ring destinations rather than beelining.
- **Consider the A* Pathfinding Project** (Aron Granberg) if you need runtime-movable/regenerating navmeshes or tighter control than Unity's baked NavMesh — it's the community default and integrates with Behavior Designer.
- **Group attack tokens:** a per-encounter manager grants a limited number of "attack tokens" so only N pack members commit at once; others circle. This produces the classic soulslike group choreography instead of a gang-pile.
- **Movement + attack blending:** freeze/blend agent movement during attack active frames (or let root motion drive), then hand control back.

#### F. Parry / perfect-guard / dodge timing windows
- Model the player defensive window as **startup → perfect window → block → recovery** frames (mirrors the enemy attack frame data).
- **Generosity is a design choice:** Sekiro's ~200 ms deflect is forgiving and *degrades gracefully* — a missed deflect becomes a normal block. Lies of P's ~155 ms Perfect Guard is tighter and punishes mashing with a lockout. Recommendation for accessibility: make a missed perfect-guard fall through to a normal block (no dead input), and consider "coyote-time"-style leniency (a frame or two of grace) for newcomer-friendly tuning.
- **Fairness depends on the enemy side:** perfect guard/parry only works if enemy attacks have *consistent, authored timing* and clear tells. This is why frame data lives in the `AttackDefinition` and the active frames are event-driven — the parry window is meaningful only because the strike lands on a predictable frame.
- Implement the check by having the incoming hit query the defender's current state: if within perfect window → deflect (negate damage, deal posture/stagger to attacker, maybe break weapon); if within block → chip + stamina + rally; else → full hit.

#### G. Performance / scalability
- **Time-slice AI:** don't run every brain's decision logic every frame. Update decision-making on a staggered cadence (e.g., round-robin buckets, or every N frames) while keeping animation/hitboxes real-time.
- **LOD the AI:** distant/inactive enemies drop to a cheap idle; only fully simulate combatants near the player. FromSoft keeps aggro/targeting outside the per-frame Goal logic so scripts stay lean — do the same (a central perception system, not per-agent raycasts every frame).
- **Throttle NavMesh destination updates** and avoid setting `destination` every frame.
- Avoid `OnTriggerStay` damage; cache component lookups; pool projectiles/VFX.

#### H. Useful resources & tools found during research
- **Reverse-engineering FromSoft AI:** "The Low-Tech AI of Elden Ring" (nega.tv) — the clearest write-up of the Goal/pushdown-automaton, weighted selection, and interrupt system; based on the `eladidu/readable-ds-lua` project and `katalash/DSLuaDecompiler` (Lua/HavokScript decompiler). The Souls Modding Wiki documents Havok Behavior and `CustomManualSelectorGenerator` animation selection.
- **Unity combat systems / repos:** `thbaylson/Unity-Third-Person-Combat` (from Nathan Farrer's "Unity 3rd Person: Combat & Traversal" course — lock-on, combos, dodge, enemy AI); `Gigadros/SoulsLike`; hitbox/hurtbox tutorials on gamedeveloper.com ("Hitboxes and Hurtboxes in Unity") and Medium. For Unreal reference (portable concepts): `georgehuan1994/Unreal-Melee-Combat-System`, `Cussk/Soulslike-Combat-System`.
- **Design analysis:** Game Maker's Toolkit "What Makes a Good Combat System?"; "The Art & Science of Sekiro's Combat" (SuperJump); Game Wisdom "The Impact of Dark Souls on Boss Design"; gamedeveloper.com "Enemy Attacks and Telegraphing."
- **Asset Store tools:** Animancer (animation), NodeCanvas / Behavior Designer (visual BT+FSM), A* Pathfinding Project (movement), Final IK (foot/hand IK & hit reactions), plus Cinemachine for lock-on camera.

## Recommendations

**Stage 1 — Vertical slice (one enemy, one attack):** Build the `EnemyBrain` stack FSM with just Idle/Approach/Attack/Recovery. Author one `AttackDefinition` SO. Wire Animation Events to toggle a trigger-collider hitbox on the `Hitbox` layer; do damage via an `IDamageable` interface. Add `PoiseComponent` and a stagger state. **Benchmark to advance:** the attack has a visible tell, a punishable recovery window, and staggers correctly. Use Animancer (free Lite tier is enough to start) if you want to iterate fast without Animator graphs.

**Stage 2 — Intentional selection & movement:** Add the `CombatDecisionMaker` with distance-banded weight tables, per-move cooldowns, and last-used history. Add NavMesh strafing/repositioning throttled off-frame. **Benchmark:** playtesters describe the enemy as "reading" them, not "random"; no move spams back-to-back.

**Stage 3 — Boss & defensive depth:** Add multi-phase logic (swap `MovesetSO` per phase at HP thresholds with a transition state), combo trees via `comboFollowups`, and interrupt reactions (punish healing/casting, watch region behind the boss). Implement the player-side parry window (perfect → block → hit fall-through). **Benchmark:** the boss changes character between phases and its combos vary without feeling scripted.

**Stage 4 — Scale & polish:** Time-slice AI decisions, LOD distant enemies, add group attack tokens, and add hit-reaction IK (Final IK) and camera polish (Cinemachine). **Benchmark:** 15–30 simultaneous combat AIs hold target frame-rate.

**Thresholds that should change your approach:** If you need **non-programmer designers authoring logic**, adopt NodeCanvas/Behavior Designer at Stage 2. If you have **only a handful of bespoke bosses**, stay hand-rolled (the FromSoft lesson). If enemies need **runtime-changing navmeshes**, switch to A* Pathfinding Project. If you find yourself wanting **emergent squad tactics** (not soulslike), only then consider Utility AI/GOAP.

## Caveats
- **The FromSoft architecture details come from community reverse-engineering of decompiled Havok Script/Lua, not official documentation.** They are highly credible and internally consistent (and match observed in-game behavior), but FromSoftware has not published these systems. Treat class/term names ("Goal," "Actor," interrupts) as reconstructed, not official.
- **Frame-window numbers for parries are community-measured**, not developer-published: Sekiro ~200 ms (~12 frames) and Lies of P ~155 ms (~9–9.3 frames) come from guides/modders and can vary with frame rate and interpretation (FromSoft's 30fps animation reference causes cross-game confusion). Use them as design starting points, not gospel.
- **I found no dedicated GDC/technical talk from Round8/Neowiz dissecting Lies of P's enemy AI architecture**; the design intent quotes are from press interviews with director Jiwon Choi. Some sourcing on enemy archetypes and boss aggression scaling relies on high-quality community analysis and wikis (Fextralife) rather than primary developer statements.
- **Poise/posture specifics differ per game and per patch** (Elden Ring poise values changed across patches); the mechanics described are the general models, and exact thresholds should be tuned for your own game rather than copied.
- Unity specifics (Animancer pricing, package names) are current as of the research date and may change.
