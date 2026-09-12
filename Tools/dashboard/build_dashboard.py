"""vibegame1 development dashboard — one self-contained HTML page from the repo's own truth.

    python Tools/dashboard/build_dashboard.py            # writes Tools/dashboard/out/index.html
    python Tools/dashboard/build_dashboard.py --open     # ...and opens it in the default browser
    python Tools/dashboard/build_dashboard.py --serve    # ...and serves it on http://127.0.0.1:8765

What it gathers (read-only, no Unity, no network):
  * every project doc: AGENTS.md, README.md, CREDITS.md, docs/*.md, specialist briefs and shared workflows
  * git: branch, last 60 commits, the working tree's changed / new file counts
  * the EditMode results the Unity runner writes (TestResults.xml): pass / fail / skip, slowest tests
  * the feature-suite line in docs/VERIFICATION-REPORT.md
  * the engineering log's entries, the backlog's sections, script counts
VISUALS, all derived from the sources rather than drawn by hand:
  * the CODE GRAPH: every class under Assets/Scripts and which classes it references (force layout)
  * the EVENT BUS: every GameEvents event, who raises it and who listens
  * the DATAFLOW MAPS: each map in docs/DATAFLOW.md turned into a flowchart from its own arrows
  * the LEVEL MAP: Level_01_Level.asset drawn top-down and in elevation (platforms, spawns, pickups,
    checkpoints, balloons, water, player start)
  * the ENEMY ROSTER: every EnemyData asset with its moveset and each attack's tell timeline
Markdown renders in the browser (marked), graphs with d3, flowcharts with mermaid — all from cdnjs; offline
the docs fall back to plain text and the diagram views say so. Invoked by the /dashboard skill.
"""
import argparse, datetime, json, os, re, subprocess, sys, webbrowser
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Tools" / "dashboard" / "out"
OUT = OUT_DIR / "index.html"
TEST_XML = Path(os.environ.get("LOCALAPPDATA", "")).parent / "LocalLow" / "vibegame1" / "vibegame1" / "TestResults.xml"

DOC_ORDER = [
    ("docs/HANDOFF.md", "Handoff"),
    ("AGENTS.md", "Agent contract (tool-neutral)"),
    ("CLAUDE.md", "Claude Code adapter"),
    ("README.md", "README"),
    ("docs/VERIFICATION-REPORT.md", "Verification report"),
    ("docs/BACKLOG.md", "Backlog"),
    ("docs/ARCHITECTURE.md", "Architecture"),
    ("docs/DATAFLOW.md", "Dataflow (system maps)"),
    ("docs/ENGINEERING-LOG.md", "Engineering log"),
    ("docs/MOVEMENT-PRINCIPLES.md", "Movement principles"),
    ("docs/ANIMATION-VFX.md", "Animation & VFX"),
    ("docs/AUTHORING.md", "Authoring content"),
    ("docs/LEVEL-VOCABULARY.md", "Level map vocabulary"),
    ("docs/TOOLING.md", "Tooling"),
    ("docs/SESSION-PROTOCOL.md", "Session protocol"),
    ("docs/GHOST-RACING.md", "Ghost racing"),
    ("docs/multiplayer-system-design.md", "Multiplayer design"),
    ("Assets/Scenes/README_Sandbox.md", "Sandbox scene"),
    ("Assets/Resources/Audio/Radio/README.md", "Radio audio"),
    ("CREDITS.md", "Credits"),
]

# ---------------------------------------------------------------------------- helpers

def sh(*args):
    try:
        command = list(args)
        if command and command[0] == "git":
            command[1:1] = ["-c", "safe.directory=" + ROOT.as_posix()]
        return subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                              errors="replace", timeout=30).stdout.strip()
    except Exception:
        return ""


def read(p):
    try:
        return (ROOT / p).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def headings(text, level=2):
    return [h.strip() for h in re.findall(r"^" + "#" * level + r" (.+)$", text, re.M)]


# ---------------------------------------------------------------------------- docs / git / tests

def collect_docs():
    docs, seen = [], set()
    for rel, title in DOC_ORDER:
        text = read(rel)
        if text:
            docs.append({"path": rel, "title": title, "text": text}); seen.add(rel)
    for p in sorted((ROOT / "docs").rglob("*.md")):
        rel = p.relative_to(ROOT).as_posix()
        if rel not in seen:
            section = p.parent.name.replace("-", " ").title()
            title = p.stem.replace("-", " ").title()
            docs.append({"path": rel, "title": (section + " · " + title) if p.parent.name != "docs" else title,
                         "text": read(rel)}); seen.add(rel)
    # These are plain Markdown contracts, not harness-owned personalities. Surface them in the same
    # dashboard so Codex, Claude Code, Cursor, Zed, Aider, Jules or a human can adopt the lane directly.
    for p in sorted((ROOT / ".claude" / "agents").glob("*.md")):
        rel = p.relative_to(ROOT).as_posix()
        if rel not in seen:
            docs.append({"path": rel, "title": "Specialist brief · " + p.stem.replace("-", " ").title(),
                         "text": read(rel)}); seen.add(rel)
    for p in sorted((ROOT / ".claude" / "skills").glob("*/SKILL.md")):
        rel = p.relative_to(ROOT).as_posix()
        if rel not in seen:
            docs.append({"path": rel, "title": "Shared workflow · " + p.parent.name.replace("-", " ").title(),
                         "text": read(rel)}); seen.add(rel)
    return docs


def git_info():
    branch = sh("git", "rev-parse", "--abbrev-ref", "HEAD")
    commits = []
    for line in sh("git", "log", "--date=short", "--pretty=format:%h%x1f%ad%x1f%an%x1f%s", "-n", "60").splitlines():
        parts = line.split("\x1f")
        if len(parts) == 4:
            commits.append({"hash": parts[0], "date": parts[1], "author": parts[2], "subject": parts[3]})
    changed = new = deleted = 0
    files = []
    for line in sh("git", "status", "--short").splitlines():
        code, path = line[:2], line[3:]
        if path.endswith(".meta"):
            continue
        if "?" in code: new += 1
        elif "D" in code: deleted += 1
        else: changed += 1
        files.append({"code": code.strip() or "?", "path": path})
    return {"branch": branch, "commits": commits, "changed": changed, "new": new, "deleted": deleted, "files": files[:400]}


def test_results():
    if not TEST_XML.exists():
        return None
    try:
        root = ET.parse(TEST_XML).getroot()
    except ET.ParseError:
        return None
    cases = [{"name": tc.get("fullname", ""), "result": tc.get("result", ""), "seconds": float(tc.get("duration") or 0)}
             for tc in root.iter("test-case")]
    suites = {}
    for c in cases:
        cls = c["name"].split(".")[-2] if c["name"].count(".") >= 2 else c["name"]
        s = suites.setdefault(cls, {"passed": 0, "failed": 0, "skipped": 0})
        s["passed" if c["result"] == "Passed" else "failed" if c["result"] == "Failed" else "skipped"] += 1
    return {"total": int(root.get("total") or 0), "passed": int(root.get("passed") or 0),
            "failed": int(root.get("failed") or 0), "skipped": int(root.get("skipped") or 0),
            "duration": float(root.get("duration") or 0),
            "when": datetime.datetime.fromtimestamp(TEST_XML.stat().st_mtime).strftime("%Y-%m-%d %H:%M"),
            "failedNames": [c["name"] for c in cases if c["result"] == "Failed"],
            "skippedNames": [c["name"] for c in cases if c["result"] not in ("Passed", "Failed")],
            "slow": sorted(cases, key=lambda c: -c["seconds"])[:8], "suites": suites, "path": str(TEST_XML)}


def feature_suite(report_text):
    m = re.search(r"\*\*(\d+)\s*passed\s*/\s*(\d+)\s*failed\s*/\s*(\d+)\s*skipped\*\*", report_text) or \
        re.search(r"(\d+) passed / (\d+) failed / (\d+) skipped", report_text)
    return {"passed": int(m.group(1)), "failed": int(m.group(2)), "skipped": int(m.group(3))} if m else None


# ---------------------------------------------------------------------------- code graph

CLASS_RE = re.compile(r"^\s*(?:public|internal|private|protected|static|abstract|sealed|partial|\s)*\s*(class|struct|interface|enum)\s+(\w+)", re.M)
SUMMARY_RE = re.compile(r"///\s*<summary>(.*?)</summary>", re.S)
NOISE = {"Update", "Awake", "Start", "Object", "Random", "Color", "Vector3", "Vector2", "Mathf", "Debug", "Time", "Layers"}


def code_graph():
    files = []
    for p in sorted((ROOT / "Assets" / "Scripts").rglob("*.cs")):
        text = read(p.relative_to(ROOT))
        classes = [m.group(2) for m in CLASS_RE.finditer(text)]
        top = None
        for m in CLASS_RE.finditer(text):
            if m.group(1) in ("class", "struct", "interface") and (top is None or m.group(2) == p.stem):
                top = m.group(2)
        if not classes:
            continue
        sm = SUMMARY_RE.search(text)
        summary = re.sub(r"<[^>]+>", "", sm.group(1)) if sm else ""
        summary = re.sub(r"\s*///\s*", " ", summary).strip()
        summary = re.sub(r"\s+", " ", summary)[:260]
        files.append({"id": top or classes[0], "file": p.relative_to(ROOT).as_posix(), "folder": p.parent.name,
                      "lines": text.count("\n") + 1, "classes": classes, "summary": summary,
                      "singleton": bool(re.search(r"public static \w+ (I|Instance)\b", text)),
                      "mono": "MonoBehaviour" in text, "so": "ScriptableObject" in text, "_text": text})
    name_to_id = {}
    for f in files:
        for c in f["classes"]:
            if len(c) > 3 and c not in NOISE:
                name_to_id.setdefault(c, f["id"])
    edges = {}
    for f in files:
        own = set(f["classes"])
        for name, target in name_to_id.items():
            if name in own or target == f["id"]:
                continue
            n = len(re.findall(r"\b" + re.escape(name) + r"\b", f["_text"]))
            if n:
                key = (f["id"], target)
                edges[key] = edges.get(key, 0) + n
    nodes = [{k: v for k, v in f.items() if k != "_text"} for f in files]
    return {"nodes": nodes, "edges": [{"source": s, "target": t, "weight": w} for (s, t), w in edges.items()],
            "folders": sorted({f["folder"] for f in files})}


FOLDER_ROLES = {
    "Core": "the frame: game state, input, the only clock that scales, the event bus",
    "Player": "the body you pilot: motor, look, combat, parry, stamina, weapons, items",
    "Combat": "what a hit is: health, posture, parry and posture maths, the flash",
    "Enemies": "what you fight: the enemy brain (FSM), its bodies, spawners, the boss",
    "Level": "the course: level manager, checkpoints, pickups, arena gates, timer, balloons, water",
    "UI": "what you read: HUD, bars, menus, prompts",
    "Feel": "what you feel: camera kicks, FX, audio, the movement and parry feedback",
    "Progression": "what you earn: souls, bloodstains, upgrades",
    "Data": "what content IS: every ScriptableObject definition (enemies, attacks, levels, feel)",
    "Ghost": "ghost racing: recordings and the ghost player",
    "Debug": "the harness: dev keys, test menu, the feature suite, the sandbox controller",
    "Items": "held items and their effects",
}

# The runtime flow at a glance, as ARCHITECTURE.md and DATAFLOW.md describe it. Hand-written on purpose:
# the measured folder graph below says how MUCH code talks to what; this says what the game DOES.
RUNTIME_FLOW = """graph LR
  IN[InputReader<br/>the only reader of the Input System] --> MOTOR[FirstPersonMotor<br/>run, jump, dash, slide, wall run, water, balloons]
  IN --> LOOK[PlayerLook]
  IN --> PC[PlayerCombat / ParryController<br/>every hit resolves here]
  MOTOR --> STAM[PlayerStamina<br/>the movement budget]
  MOTOR -. events: jumped, dashed, slid, perfect .-> FEEL[Feel layer<br/>CameraShake, CameraFX, SlideFx, DashFx, PlayerFeedback]
  DATA[(Data assets<br/>EnemyData, attacks, movesets, GameFeel, LevelDefinition)] --> AI
  DATA --> MOTOR
  DATA --> FEEL
  AI[EnemyController<br/>state machine: chase, windup, strike, recover] --> VIS[EnemyVisuals / PuppetVisuals<br/>the telegraph: pose, clip, cue flash]
  AI -- ReceiveAttack --> PC
  PC -- deflect / block / hit --> HP[Health + Posture]
  PC --> TS[TimeScaleController<br/>hitstop, the only writer of timeScale]
  HP --> AI
  HP --> PROG[SoulsWallet / upgrades]
  PC -. GameEvents .-> HUD[HUDController<br/>bars, prompts, menus]
  HP -. GameEvents .-> HUD
  STAM -. GameEvents .-> HUD
  LEVEL[LevelManager<br/>checkpoints, arena, timer, respawn] -. GameEvents .-> HUD
  LEVEL --> AI
"""


def folder_graph(graph):
    by_id = {n["id"]: n for n in graph["nodes"]}
    agg = {}
    for e in graph["edges"]:
        a, b = by_id.get(e["source"]), by_id.get(e["target"])
        if not a or not b or a["folder"] == b["folder"]:
            continue
        key = (a["folder"], b["folder"])
        agg[key] = agg.get(key, 0) + e["weight"]
    folders = {}
    for n in graph["nodes"]:
        f = folders.setdefault(n["folder"], {"folder": n["folder"], "classes": [], "lines": 0})
        f["classes"].append(n); f["lines"] += n["lines"]
    mm = ["graph LR"]
    for f, info in sorted(folders.items()):
        top = sorted(info["classes"], key=lambda n: -n["lines"])[:6]
        mm.append(f'  subgraph {mermaid_id(f)}["{f}  ·  {len(info["classes"])} classes"]')
        for n in top:
            mm.append(f'    {mermaid_id(f + "_" + n["id"])}["{n["id"]}"]')
        mm.append("  end")
    strong = sorted(agg.items(), key=lambda kv: -kv[1])
    for (a, b), w in strong:
        # The harness (Debug) references everything by design; drawing it would bury the game's own shape.
        if w >= 6 and "Debug" not in (a, b):
            mm.append(f'  {mermaid_id(a)} -- "{w}" --> {mermaid_id(b)}')
    table = [{"folder": f, "role": FOLDER_ROLES.get(f, ""), "classes": len(info["classes"]), "lines": info["lines"],
              "key": [n["id"] for n in sorted(info["classes"], key=lambda n: -n["lines"])[:5]]}
             for f, info in sorted(folders.items(), key=lambda kv: -kv[1]["lines"])]
    return {"mermaid": chr(10).join(mm), "table": table, "edges": [{"from": a, "to": b, "weight": w} for (a, b), w in strong]}


# ---------------------------------------------------------------------------- event bus

def event_bus():
    text = read("Assets/Scripts/Core/GameEvents.cs")
    events = re.findall(r"public static event Action(?:<[^>]*>)?\s+(\w+);", text)
    sources = {}
    for p in sorted((ROOT / "Assets").rglob("*.cs")):
        rel = p.relative_to(ROOT).as_posix()
        if "/Editor/" in rel and "/Tests/" in rel:
            continue
        sources[rel] = read(rel)
    out = []
    for e in events:
        raisers = sorted({Path(r).stem for r, t in sources.items() if r.endswith("GameEvents.cs") is False and
                          (re.search(r"GameEvents\.Raise" + e + r"\(", t) or re.search(r"GameEvents\." + e + r"\?\.Invoke", t))})
        listeners = sorted({Path(r).stem for r, t in sources.items() if re.search(r"GameEvents\." + e + r"\s*\+=", t)})
        out.append({"event": e, "raisers": raisers, "listeners": listeners})
    return out


# ---------------------------------------------------------------------------- dataflow maps → mermaid

ARROW_SPLIT = re.compile(r"\s*(?:→|⇢|->|=>|⟶)\s*")
IDENT = re.compile(r"([A-Z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)?)")


def mermaid_id(s):
    return "n_" + re.sub(r"[^A-Za-z0-9_]", "_", s)


def dataflow_maps(text):
    maps = []
    # split into ### sections (fall back to ## when a section has no ###)
    parts = re.split(r"^(#{2,3} .+)$", text, flags=re.M)
    title = None
    for i in range(1, len(parts), 2):
        heading = parts[i].lstrip("# ").strip()
        body = parts[i + 1]
        blocks = re.findall(r"```(?:\w*)\n(.*?)```", body, re.S)
        if not blocks:
            continue
        edges, nodes, order = [], {}, []
        raw = "\n\n".join(blocks)
        stack = []  # (indent, node)
        for line in raw.splitlines():
            if not line.strip() or line.strip().startswith(("//", "#", "|", "*", "-", "(", "[")):
                continue
            indent = len(line) - len(line.lstrip())
            segs = [s for s in ARROW_SPLIT.split(line.strip()) if s.strip()]
            leading_arrow = bool(re.match(r"\s*(→|⇢|->|=>|⟶)", line))
            ids = []
            for seg in segs:
                m = IDENT.search(seg)
                if not m:
                    continue
                name = m.group(1).split(".")[0]
                if name.isupper() or (len(name) < 5 and "." not in m.group(1)):
                    continue
                if len(name) < 3 or name in ("The", "A", "An", "This", "That", "Rule", "NOTE", "Every", "If", "One", "Two", "Three", "Same", "No", "Not", "See", "In", "On", "At", "For", "Before", "After", "Between", "Each"):
                    continue
                if name not in nodes:
                    nodes[name] = seg.strip()[:60]; order.append(name)
                ids.append(name)
            if not ids:
                continue
            while stack and stack[-1][0] >= indent:
                stack.pop()
            if leading_arrow and stack:
                edges.append((stack[-1][1], ids[0]))
            for a, b in zip(ids, ids[1:]):
                if a != b:
                    edges.append((a, b))
            stack.append((indent, ids[-1]))
        uniq = []
        seen = set()
        for e in edges:
            if e not in seen and e[0] != e[1]:
                seen.add(e); uniq.append(e)
        if len(uniq) < 2 or len(order) > 45:
            continue
        mm = ["graph LR"]
        for n in order:
            mm.append(f'  {mermaid_id(n)}["{n}"]')
        for a, b in uniq[:80]:
            mm.append(f"  {mermaid_id(a)} --> {mermaid_id(b)}")
        maps.append({"title": heading, "mermaid": "\n".join(mm), "raw": raw[:6000], "nodes": len(order), "edges": len(uniq)})
    return maps


# ---------------------------------------------------------------------------- level asset

VEC = r"\{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}"


def yaml_list(text, key):
    m = re.search(r"^  " + key + r":\n((?:  - .*\n(?:    .*\n)*)+)", text, re.M)
    if not m:
        return []
    items = re.split(r"^  - ", m.group(1), flags=re.M)[1:]
    out = []
    for it in items:
        d = {}
        for line in it.splitlines():
            mm = re.match(r"\s*(\w+): (.*)$", line)
            if mm:
                k, v = mm.group(1), mm.group(2).strip()
                vv = re.match(VEC, v)
                d[k] = [float(vv.group(1)), float(vv.group(2)), float(vv.group(3))] if vv else v
        out.append(d)
    return out


def level_vocabulary(text):
    """Read the dashboard JSON block from the human-owned canonical vocabulary document."""
    marker = "<!-- dashboard-level-vocabulary -->"
    start = text.find(marker)
    if start < 0:
        return {"levels": [], "enemyTerms": {}}
    block = re.search(r"```json\s*(\{.*?\})\s*```", text[start:], re.S)
    if not block:
        return {"levels": [], "enemyTerms": {}}
    try:
        return json.loads(block.group(1))
    except json.JSONDecodeError:
        return {"levels": [], "enemyTerms": {}}


def level_map(vocabulary=None):
    vocabulary = vocabulary or {"levels": [], "enemyTerms": {}}
    vocab_by_file = {x.get("file", ""): x for x in vocabulary.get("levels", [])}
    levels = []
    for p in sorted((ROOT / "Assets" / "Data" / "Levels").rglob("*.asset")):
        t = read(p.relative_to(ROOT))
        if "platforms:" not in t:
            continue
        name = re.search(r"displayName: (.*)", t)
        ps = re.search(r"playerStart: " + VEC, t)
        rel = p.relative_to(ROOT).as_posix()
        vocab = vocab_by_file.get(rel, {})
        levels.append({
            "file": rel, "name": name.group(1).strip() if name else p.stem,
            "playerStart": [float(ps.group(1)), float(ps.group(2)), float(ps.group(3))] if ps else None,
            "platforms": [x for x in yaml_list(t, "platforms") if "center" in x and "size" in x],
            "ramps": yaml_list(t, "ramps"),
            "spawns": yaml_list(t, "spawns"), "pickups": yaml_list(t, "pickups"),
            "checkpoints": yaml_list(t, "checkpoints"), "balloons": yaml_list(t, "balloons"),
            "waters": yaml_list(t, "waters"), "torches": yaml_list(t, "torches"),
            "pedestals": yaml_list(t, "pedestals"), "arenas": yaml_list(t, "arenas"),
            "projectileSequences": yaml_list(t, "projectileSequences"),
            "insightRoutes": yaml_list(t, "insightRoutes"),
            "zones": vocab.get("zones", []), "enemyTerms": vocabulary.get("enemyTerms", {}),
        })
    return levels


# ---------------------------------------------------------------------------- enemies

def guid_index(folder):
    idx = {}
    data_root = ROOT / "Assets" / "Data" / folder
    for meta in data_root.rglob("*.asset.meta"):
        m = re.search(r"guid: ([0-9a-f]{32})", read(meta.relative_to(ROOT)))
        if m:
            # Keep the relative asset path: Movesets and Enemies are grouped into tool-neutral content
            # folders now, so a stem-only index silently pointed back at the retired flat layout.
            idx[m.group(1)] = meta.relative_to(ROOT).as_posix()[:-len(".meta")]
    return idx


def scalar(text, key, cast=float):
    m = re.search(r"^  " + key + r": (.+)$", text, re.M)
    if not m:
        return None
    try:
        return cast(m.group(1).strip())
    except ValueError:
        return m.group(1).strip()


def enemies():
    attack_paths, moveset_paths = guid_index("Attacks"), guid_index("Movesets")
    attacks_by_guid = {guid: Path(path).stem for guid, path in attack_paths.items()}
    attacks = {}
    for p in (ROOT / "Assets" / "Data" / "Attacks").rglob("*.asset"):
        t = read(p.relative_to(ROOT))
        attacks[p.stem] = {k: scalar(t, k) for k in ("windup", "impactDelay", "strikeDuration", "recovery", "range", "coneDeg", "damage", "lungeDistance")}
        attacks[p.stem]["unblockable"] = scalar(t, "unblockable", int) == 1
        attacks[p.stem]["clip"] = scalar(t, "clip", str) or ""
        attacks[p.stem]["name"] = p.stem
    out = []
    for p in sorted((ROOT / "Assets" / "Data" / "Enemies").rglob("*.asset")):
        t = read(p.relative_to(ROOT))
        ms = re.search(r"moveset: \{fileID: \d+, guid: ([0-9a-f]{32})", t)
        entries = []
        if ms and ms.group(1) in moveset_paths:
            mt = read(moveset_paths[ms.group(1)])
            for block in re.split(r"^  - label: ", mt, flags=re.M)[1:]:
                label = block.splitlines()[0].strip()
                hits = [attacks_by_guid.get(g, g) for g in re.findall(r"guid: ([0-9a-f]{32}), type: 2", block)]
                w = re.search(r"weight: ([\d.]+)", block); lo = re.search(r"minRange: ([\d.]+)", block); hi = re.search(r"maxRange: ([\d.]+)", block)
                entries.append({"label": label, "hits": hits, "weight": float(w.group(1)) if w else 1,
                                "minRange": float(lo.group(1)) if lo else 0, "maxRange": float(hi.group(1)) if hi else 0})
        used = []
        for e in entries:
            for h in e["hits"]:
                if h in attacks and h not in used:
                    used.append(h)
        out.append({"id": p.stem, "displayName": scalar(t, "displayName", str) or p.stem,
                    "stats": {k: scalar(t, k) for k in ("maxHP", "maxPosture", "postureRegen", "staggerSeconds", "moveSpeed", "aggroRange", "attackRange", "preferredRange", "aggression", "scale", "soulValue")},
                    "entries": entries, "attacks": [attacks[h] for h in used]})
    return out


# ---------------------------------------------------------------------------- build

def build():
    docs = collect_docs()
    by_path = {d["path"]: d["text"] for d in docs}
    vocabulary = level_vocabulary(by_path.get("docs/LEVEL-VOCABULARY.md", ""))
    data = {
        "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M"), "root": str(ROOT),
        "git": git_info(), "editMode": test_results(), "feature": feature_suite(by_path.get("docs/VERIFICATION-REPORT.md", "")),
        "logEntries": headings(by_path.get("docs/ENGINEERING-LOG.md", "")),
        "backlogSections": headings(by_path.get("docs/BACKLOG.md", "")),
        "graph": code_graph(), "events": event_bus(),
        "runtimeFlow": RUNTIME_FLOW,
        "maps": dataflow_maps(by_path.get("docs/DATAFLOW.md", "")),
        "levels": level_map(vocabulary), "enemies": enemies(), "docs": docs,
        "agentContract": {
            "path": "AGENTS.md",
            "specialists": len(list((ROOT / ".claude" / "agents").glob("*.md"))),
            "workflows": len(list((ROOT / ".claude" / "skills").glob("*/SKILL.md"))),
        },
    }
    data["folders"] = folder_graph(data["graph"])
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    OUT.write_text(TEMPLATE.replace("__DATA__", json.dumps(data, ensure_ascii=False).replace("</", "<\\/")), encoding="utf-8")
    return OUT, data


TEMPLATE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>vibegame1 — development dashboard</title>
<script src="https://cdnjs.cloudflare.com/ajax/libs/marked/12.0.2/marked.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/d3/7.9.0/d3.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.1/mermaid.min.js"></script>
<style>
:root{--bg:#0b0710;--pane:rgba(20,14,26,.72);--edge:rgba(232,226,214,.14);--bone:#e8e2d6;--dim:#9a918a;--ember:#ff7a1e;--teal:#4fe0d0;--gold:#c9a227;--blood:#b41e2e;--ok:#5fd28a;--bad:#ff5c5c;--mono:ui-monospace,Consolas,monospace}
*{box-sizing:border-box}body{margin:0;background:radial-gradient(1200px 600px at 30% -10%,#2a1220 0%,var(--bg) 60%);color:var(--bone);font:14px/1.5 system-ui,Segoe UI,Roboto,sans-serif}
a{color:var(--teal);text-decoration:none}a:hover{text-decoration:underline}
.top{display:flex;gap:16px;align-items:center;padding:14px 22px;border-bottom:1px solid var(--edge);background:linear-gradient(180deg,rgba(255,122,30,.10),transparent)}
.top h1{font-size:18px;margin:0;letter-spacing:.06em}.top .meta{color:var(--dim);font-size:12px}
.top input{margin-left:auto;background:rgba(0,0,0,.45);border:1px solid var(--edge);border-radius:999px;color:var(--bone);padding:7px 14px;width:280px;outline:none}
.wrap{display:grid;grid-template-columns:260px 1fr;min-height:calc(100vh - 54px)}
nav{border-right:1px solid var(--edge);padding:14px 10px;position:sticky;top:0;height:calc(100vh - 54px);overflow:auto}
nav h3{font-size:11px;letter-spacing:.14em;color:var(--dim);margin:14px 8px 6px;text-transform:uppercase}
nav button{display:block;width:100%;text-align:left;background:none;border:0;color:var(--bone);padding:7px 10px;border-radius:8px;cursor:pointer;font:inherit}
nav button:hover{background:rgba(255,255,255,.05)}nav button.active{background:rgba(79,224,208,.12);color:var(--teal)}
main{padding:20px 26px;max-width:1400px}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px;margin-bottom:18px}
.tile{background:var(--pane);border:1px solid var(--edge);border-radius:12px;padding:14px 16px;position:relative;overflow:hidden;box-shadow:0 10px 30px rgba(0,0,0,.35)}
.tile:before{content:"";position:absolute;left:0;right:0;top:0;height:1px;background:linear-gradient(90deg,var(--ember),var(--bone))}
.tile .k{font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:var(--dim)}.tile .v{font-size:26px;font-weight:600;margin-top:2px}.tile .s{font-size:12px;color:var(--dim)}
.ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--gold)}
.pane{background:var(--pane);border:1px solid var(--edge);border-radius:12px;padding:18px 22px;margin-bottom:16px;box-shadow:0 10px 30px rgba(0,0,0,.35)}
.pane h2{margin:0 0 10px;font-size:15px;letter-spacing:.08em;text-transform:uppercase;color:var(--gold)}
table{border-collapse:collapse;width:100%;font-size:13px}td,th{padding:5px 8px;border-bottom:1px solid rgba(255,255,255,.06);text-align:left;vertical-align:top}th{color:var(--dim);font-weight:500}
.mono{font-family:var(--mono);font-size:12px}.chip{display:inline-block;padding:1px 8px;border-radius:999px;background:rgba(255,255,255,.07);font-size:11px;margin:2px 4px 2px 0}
.doc{background:var(--pane);border:1px solid var(--edge);border-radius:12px;padding:8px 30px 30px}
.doc h1,.doc h2,.doc h3{color:var(--bone)}.doc h1{font-size:24px}.doc h2{font-size:18px;border-bottom:1px solid var(--edge);padding-bottom:4px;margin-top:28px}.doc h3{font-size:15px;color:var(--gold)}
.doc code{font-family:var(--mono);font-size:12px;background:rgba(0,0,0,.4);padding:1px 5px;border-radius:4px}.doc pre{background:rgba(0,0,0,.5);border:1px solid var(--edge);padding:12px;border-radius:8px;overflow:auto;font-size:12px}.doc pre code{background:none;padding:0}
.doc table{font-size:13px;margin:10px 0}.doc blockquote{border-left:3px solid var(--ember);margin:0;padding:2px 14px;color:var(--dim)}.doc img{max-width:100%}
.hit{background:rgba(201,162,39,.35);border-radius:3px}.small{font-size:12px;color:var(--dim)}
.two{display:grid;grid-template-columns:1fr 1fr;gap:16px}@media(max-width:1000px){.two{grid-template-columns:1fr}.wrap{grid-template-columns:1fr}nav{position:static;height:auto}}
svg text{fill:var(--bone);font-family:system-ui,sans-serif}.graph{width:100%;height:720px;background:rgba(0,0,0,.35);border-radius:10px;border:1px solid var(--edge)}
.legend span{display:inline-flex;align-items:center;gap:6px;margin-right:12px;font-size:12px;color:var(--dim)}.legend i{width:12px;height:12px;border-radius:50%;display:inline-block}
.mermaid{background:rgba(0,0,0,.35);border-radius:10px;border:1px solid var(--edge);padding:10px;overflow:auto}
pre.raw{background:rgba(0,0,0,.5);border:1px solid var(--edge);padding:10px;border-radius:8px;overflow:auto;font-size:11.5px;line-height:1.35;max-height:360px}
.card{background:rgba(0,0,0,.3);border:1px solid var(--edge);border-radius:10px;padding:12px 14px;margin-bottom:12px}
.bar{height:8px;border-radius:4px;background:rgba(255,255,255,.08);overflow:hidden}.bar i{display:block;height:100%}
.tl{display:flex;height:12px;border-radius:3px;overflow:hidden;background:rgba(255,255,255,.06)}.tl i{display:block;height:100%}
.ctl{display:flex;gap:10px;align-items:center;margin-bottom:8px;font-size:12px;color:var(--dim)}.ctl select,.ctl input[type=range]{background:rgba(0,0,0,.4);color:var(--bone);border:1px solid var(--edge);border-radius:6px}
</style></head><body>
<div class="top"><h1>VIBEGAME1</h1><span class="meta" id="meta"></span><input id="q" placeholder="search every doc, brief and workflow…"></div>
<div class="wrap"><nav id="nav"></nav><main id="main"></main></div>
<script>
const D=__DATA__;
const $=s=>document.querySelector(s);const esc=s=>String(s??'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
const FOLDER_COLOURS={Player:'#4fe0d0',Enemies:'#ff7a1e',Combat:'#b41e2e',Feel:'#c9a227',UI:'#e8e2d6',Core:'#8fb5d9',Data:'#9a7bd9',Level:'#5fd28a',Ghost:'#7fd3ff',Debug:'#777',Items:'#f2b8ff',Audio:'#ffd166'};
const fc=f=>FOLDER_COLOURS[f]||'#aaa';
$('#meta').textContent=`branch ${D.git.branch||'?'} · generated ${D.generated} · ${D.root}`;
try{mermaid.initialize({startOnLoad:false,theme:'dark',securityLevel:'loose',flowchart:{curve:'basis',nodeSpacing:30,rankSpacing:40}})}catch(e){}
function nav(){const n=$('#nav');let h='<h3>Overview</h3><button data-v="home" class="active">Dashboard</button><button data-v="agents">Agent contract</button><button data-v="changes">Change log</button><button data-v="tests">Tests</button>';
h+='<h3>Visuals</h3><button data-v="graph">Code graph</button><button data-v="events">Event bus</button><button data-v="maps">Dataflow maps</button><button data-v="level">Level map</button><button data-v="enemies">Enemy roster</button>';
h+='<h3>Documentation</h3>';D.docs.forEach((d,i)=>h+=`<button data-v="doc:${i}">${esc(d.title)}</button>`);n.innerHTML=h;
n.querySelectorAll('button').forEach(b=>b.onclick=()=>{n.querySelectorAll('button').forEach(x=>x.classList.remove('active'));b.classList.add('active');show(b.dataset.v)});}
function tile(k,v,s,cls){return `<div class="tile"><div class="k">${k}</div><div class="v ${cls||''}">${v}</div><div class="s">${s||''}</div></div>`}
function render(md){try{return marked.parse(md,{mangle:false,headerIds:false})}catch(e){return '<pre>'+esc(md)+'</pre>'}}
// ---------------------------------------------------------------- home
function home(){const e=D.editMode,f=D.feature,g=D.git;let h='<div class="tiles">';
h+=tile('EditMode tests',e?`<span class="${e.failed?'bad':'ok'}">${e.passed}/${e.total}</span>`:'—',e?`${e.failed} failed · ${e.skipped} skipped · ${e.duration.toFixed(0)} s · ${e.when}`:'no TestResults.xml yet');
h+=tile('Feature suite',f?`<span class="${f.failed?'bad':'ok'}">${f.passed}</span>`:'—',f?`${f.failed} failed · ${f.skipped} skipped (from the verification report)`:'not recorded');
h+=tile('Working tree',`${g.changed+g.new+g.deleted}`,`${g.changed} changed · ${g.new} new · ${g.deleted} deleted (uncommitted, .meta excluded)`,g.changed+g.new+g.deleted?'warn':'ok');
h+=tile('Commits',g.commits.length?g.commits[0].hash:'—',g.commits.length?`${g.commits[0].date} · ${esc(g.commits[0].subject)}`:'');
h+=tile('Scripts',D.graph.nodes.length,`classes under Assets/Scripts · ${D.graph.edges.length} references`);
h+=tile('Agent contract',D.agentContract.path,`${D.agentContract.specialists} tool-neutral specialist briefs · ${D.agentContract.workflows} shared workflows`,'ok');
h+=tile('Engineering log',D.logEntries.length,'entries');h+='</div>';
const hand=D.docs.find(d=>d.path==='docs/HANDOFF.md');
h+='<div class="two"><div class="pane"><h2>Handoff — state of play</h2><div class="doc" style="padding:0 16px 10px">'+(hand?render(hand.text):'<p class="small">no docs/HANDOFF.md</p>')+'</div></div>';
h+='<div><div class="pane"><h2>Latest engineering-log entries</h2><ol>'+D.logEntries.slice(0,10).map(x=>`<li>${esc(x)}</li>`).join('')+'</ol></div></div></div>';
h+='<div class="pane"><h2>How the game runs — the runtime flow</h2><p class="small">Solid arrows are calls; dotted arrows are <code>GameEvents</code> broadcasts (gameplay raises, the HUD and the feel layer listen, nothing listens back into gameplay). Data assets feed everything and are never written at runtime. From ARCHITECTURE.md and DATAFLOW.md.</p><pre class="mermaid">'+esc(D.runtimeFlow)+'</pre></div>';
h+='<div class="pane"><h2>The code, by system</h2><p class="small">Every folder under Assets/Scripts as a group with its largest classes; an arrow between groups is how many times the code in one references classes in the other (≥ 6 shown; the Debug harness is left out of the arrows because it touches everything by design). Measured from the sources, not drawn by hand.</p><pre class="mermaid">'+esc(D.folders.mermaid)+'</pre>';
h+='<table><tr><th>system</th><th>what it is</th><th>classes</th><th>lines</th><th>biggest classes</th></tr>'+D.folders.table.map(f=>`<tr><td><b style="color:${fc(f.folder)}">${esc(f.folder)}</b></td><td>${esc(f.role)}</td><td>${f.classes}</td><td>${f.lines}</td><td class="small">${f.key.map(esc).join(', ')}</td></tr>`).join('')+'</table></div>';
setTimeout(()=>{try{mermaid.run({nodes:document.querySelectorAll('pre.mermaid')})}catch(e){}},0);return h}
// ---------------------------------------------------------------- tool-neutral agent contract
function agentsView(){const contract=D.docs.find(d=>d.path==='AGENTS.md');const briefs=D.docs.filter(d=>d.path.startsWith('.claude/agents/'));const workflows=D.docs.filter(d=>d.path.startsWith('.claude/skills/'));
const card=d=>{const i=D.docs.indexOf(d);const first=(d.text.match(/^#\s+(.+)$/m)||[])[1]||d.title;return `<div class="card"><b>${esc(d.title)}</b><div class="small mono">${esc(d.path)}</div><p class="small">${esc(first)}</p><button data-doc="${i}">read contract</button></div>`};
let h='<div class="pane"><h2>Tool-neutral agent contract</h2><p>This project has one source of truth: <code>AGENTS.md</code>. Harness-specific files are adapters; the specialist briefs and workflows below are plain Markdown that any coding agent or human can follow.</p>';
if(contract){const i=D.docs.indexOf(contract);h+=`<button data-doc="${i}">open AGENTS.md</button>`}h+='</div>';
h+=`<div class="two"><div class="pane"><h2>Specialist briefs (${briefs.length})</h2>${briefs.map(card).join('')}</div><div class="pane"><h2>Shared workflows (${workflows.length})</h2>${workflows.map(card).join('')}</div></div>`;
setTimeout(()=>document.querySelectorAll('[data-doc]').forEach(b=>b.onclick=()=>{const i=+b.dataset.doc;document.querySelector(`[data-v='doc:${i}']`).click()}),0);return h}
// ---------------------------------------------------------------- changes / tests
function changes(){const g=D.git;let h='<div class="pane"><h2>Commits (last '+g.commits.length+')</h2><table><tr><th>hash</th><th>date</th><th>subject</th></tr>'+g.commits.map(c=>`<tr><td class="mono">${c.hash}</td><td>${c.date}</td><td>${esc(c.subject)}</td></tr>`).join('')+'</table></div>';
h+='<div class="pane"><h2>Uncommitted files ('+g.files.length+')</h2><table>'+g.files.map(f=>`<tr><td class="mono" style="width:40px">${esc(f.code)}</td><td class="mono">${esc(f.path)}</td></tr>`).join('')+'</table></div>';
h+='<div class="pane"><h2>Engineering log — every entry</h2><ol>'+D.logEntries.map(x=>`<li>${esc(x)}</li>`).join('')+'</ol></div>';return h}
function tests(){const e=D.editMode;if(!e)return '<div class="pane"><h2>Tests</h2><p>No TestResults.xml. Run the EditMode suite in the editor first.</p></div>';
let h='<div class="tiles">'+tile('Passed',e.passed,'','ok')+tile('Failed',e.failed,'',e.failed?'bad':'ok')+tile('Skipped',e.skipped,'',e.skipped?'warn':'ok')+tile('Duration',e.duration.toFixed(0)+' s',e.when)+'</div>';
if(e.failedNames.length)h+='<div class="pane"><h2 class="bad">Failed</h2><ul class="mono">'+e.failedNames.map(x=>`<li>${esc(x)}</li>`).join('')+'</ul></div>';
if(e.skippedNames.length)h+='<div class="pane"><h2 class="warn">Skipped / ignored</h2><ul class="mono">'+e.skippedNames.map(x=>`<li>${esc(x)}</li>`).join('')+'</ul></div>';
h+='<div class="two"><div class="pane"><h2>Per suite</h2><table><tr><th>suite</th><th>pass</th><th>fail</th><th>skip</th></tr>'+Object.entries(e.suites).sort().map(([k,v])=>`<tr><td>${esc(k)}</td><td class="ok">${v.passed}</td><td class="${v.failed?'bad':''}">${v.failed}</td><td>${v.skipped}</td></tr>`).join('')+'</table></div>';
h+='<div class="pane"><h2>Slowest</h2><table>'+e.slow.map(s=>`<tr><td>${s.seconds.toFixed(1)} s</td><td class="mono">${esc(s.name.split('.').slice(-2).join('.'))}</td></tr>`).join('')+'</table></div></div>';
const f=D.feature;h+='<div class="pane"><h2>Feature suite (play mode)</h2>'+(f?`<p><span class="ok">${f.passed} passed</span> · ${f.failed} failed · ${f.skipped} skipped — as recorded in docs/VERIFICATION-REPORT.md</p>`:'<p>not recorded</p>')+'</div>';return h}
// ---------------------------------------------------------------- code graph
function graphView(){let h='<div class="pane"><h2>Code graph — who references whom</h2><div class="ctl"><label>folder <select id="gf"><option value="">all</option>'+D.graph.folders.map(f=>`<option>${f}</option>`).join('')+'</select></label><label>min references <input id="gw" type="range" min="1" max="12" value="2"><span id="gwv">2</span></label><span>drag nodes · click a node for its links · ring = singleton · square = ScriptableObject</span></div>';
h+='<div class="legend">'+D.graph.folders.map(f=>`<span><i style="background:${fc(f)}"></i>${f}</span>`).join('')+'</div><svg class="graph" id="gsvg"></svg><div id="ginfo" class="small" style="margin-top:8px">—</div></div>';
setTimeout(drawGraph,0);return h}
let sim;
function drawGraph(){if(!window.d3){$('#ginfo').textContent='d3 did not load (offline?)';return}const svg=d3.select('#gsvg');svg.selectAll('*').remove();const W=svg.node().clientWidth,H=svg.node().clientHeight;
const folder=$('#gf').value,minw=+$('#gw').value;$('#gwv').textContent=minw;
const nodes=D.graph.nodes.filter(n=>!folder||n.folder===folder).map(n=>({...n}));const ids=new Set(nodes.map(n=>n.id));
const links=D.graph.edges.filter(e=>e.weight>=minw&&ids.has(e.source)&&ids.has(e.target)).map(e=>({...e}));
const deg={};links.forEach(l=>{deg[l.source]=(deg[l.source]||0)+1;deg[l.target]=(deg[l.target]||0)+1});
const g=svg.append('g');svg.call(d3.zoom().scaleExtent([.3,3]).on('zoom',ev=>g.attr('transform',ev.transform)));
svg.append('defs').append('marker').attr('id','arr').attr('viewBox','0 -4 8 8').attr('refX',14).attr('markerWidth',6).attr('markerHeight',6).attr('orient','auto').append('path').attr('d','M0,-4L8,0L0,4').attr('fill','#777');
const link=g.append('g').selectAll('line').data(links).enter().append('line').attr('stroke','#888').attr('stroke-opacity',.35).attr('stroke-width',l=>Math.min(4,.6+Math.log2(l.weight))).attr('marker-end','url(#arr)');
const node=g.append('g').selectAll('g').data(nodes).enter().append('g').style('cursor','pointer');
const r=n=>5+Math.sqrt(n.lines)/3;
node.each(function(n){const s=d3.select(this);if(n.so)s.append('rect').attr('x',-r(n)).attr('y',-r(n)).attr('width',2*r(n)).attr('height',2*r(n)).attr('rx',3).attr('fill',fc(n.folder)).attr('fill-opacity',.85);else s.append('circle').attr('r',r(n)).attr('fill',fc(n.folder)).attr('fill-opacity',.85);if(n.singleton)s.append('circle').attr('r',r(n)+3).attr('fill','none').attr('stroke',fc(n.folder)).attr('stroke-width',1.5)});
node.append('text').attr('dx',n=>r(n)+3).attr('dy',4).attr('font-size',10).text(n=>n.id);node.append('title').text(n=>`${n.file}\n${n.lines} lines\n${n.summary}`);
node.on('click',(ev,n)=>{const out=links.filter(l=>(l.source.id||l.source)===n.id).map(l=>(l.target.id||l.target)+' ×'+l.weight);const inn=links.filter(l=>(l.target.id||l.target)===n.id).map(l=>(l.source.id||l.source)+' ×'+l.weight);
$('#ginfo').innerHTML=`<b>${n.id}</b> <span class="mono">${n.file}</span> · ${n.lines} lines${n.singleton?' · singleton':''}${n.mono?' · MonoBehaviour':''}${n.so?' · ScriptableObject':''}<br>${esc(n.summary)}<br><b>uses:</b> ${out.map(esc).join(', ')||'—'}<br><b>used by:</b> ${inn.map(esc).join(', ')||'—'}`;
link.attr('stroke',l=>(l.source.id===n.id||l.target.id===n.id)?'#ff7a1e':'#888').attr('stroke-opacity',l=>(l.source.id===n.id||l.target.id===n.id)?.9:.15)});
node.call(d3.drag().on('start',(ev,d)=>{if(!ev.active)sim.alphaTarget(.3).restart();d.fx=d.x;d.fy=d.y}).on('drag',(ev,d)=>{d.fx=ev.x;d.fy=ev.y}).on('end',(ev,d)=>{if(!ev.active)sim.alphaTarget(0);d.fx=null;d.fy=null}));
if(sim)sim.stop();sim=d3.forceSimulation(nodes).force('link',d3.forceLink(links).id(n=>n.id).distance(l=>70).strength(.4)).force('charge',d3.forceManyBody().strength(-160)).force('center',d3.forceCenter(W/2,H/2)).force('collide',d3.forceCollide(n=>r(n)+14));
sim.on('tick',()=>{link.attr('x1',l=>l.source.x).attr('y1',l=>l.source.y).attr('x2',l=>l.target.x).attr('y2',l=>l.target.y);node.attr('transform',n=>`translate(${n.x},${n.y})`)});
$('#gf').onchange=drawGraph;$('#gw').oninput=drawGraph}
// ---------------------------------------------------------------- event bus
function eventsView(){const E=D.events;let h='<div class="pane"><h2>Event bus — GameEvents ('+E.length+' events)</h2><p class="small">Raisers on the left, the event in the middle, listeners on the right. Gameplay raises; the HUD and the feel layer listen; nothing listens back into gameplay.</p><div id="esvg"></div></div>';
h+='<div class="pane"><h2>Table</h2><table><tr><th>event</th><th>raised by</th><th>listened to by</th></tr>'+E.map(e=>`<tr><td class="mono">${esc(e.event)}</td><td>${e.raisers.map(x=>`<span class="chip">${esc(x)}</span>`).join('')||'<span class="small">—</span>'}</td><td>${e.listeners.map(x=>`<span class="chip">${esc(x)}</span>`).join('')||'<span class="small">—</span>'}</td></tr>`).join('')+'</table></div>';
setTimeout(drawEvents,0);return h}
function drawEvents(){const el=$('#esvg');if(!el||!window.d3)return;const E=D.events;const raisers=[...new Set(E.flatMap(e=>e.raisers))].sort(),listeners=[...new Set(E.flatMap(e=>e.listeners))].sort();
const rowH=22,H=Math.max(raisers.length,E.length,listeners.length)*rowH+40,W=el.clientWidth||1000;const svg=d3.select(el).append('svg').attr('width',W).attr('height',H);
const yR=d3.scalePoint().domain(raisers).range([20,20+(raisers.length-1)*rowH]),yE=d3.scalePoint().domain(E.map(e=>e.event)).range([20,20+(E.length-1)*rowH]),yL=d3.scalePoint().domain(listeners).range([20,20+(listeners.length-1)*rowH]);
const x0=170,x1=W/2,x2=W-190;const col=d3.scaleOrdinal(d3.schemeTableau10).domain(E.map(e=>e.event));
E.forEach(e=>{e.raisers.forEach(r=>svg.append('path').attr('d',`M${x0},${yR(r)} C${(x0+x1)/2},${yR(r)} ${(x0+x1)/2},${yE(e.event)} ${x1-6},${yE(e.event)}`).attr('fill','none').attr('stroke',col(e.event)).attr('stroke-opacity',.55));
e.listeners.forEach(l=>svg.append('path').attr('d',`M${x1+6},${yE(e.event)} C${(x1+x2)/2},${yE(e.event)} ${(x1+x2)/2},${yL(l)} ${x2},${yL(l)}`).attr('fill','none').attr('stroke',col(e.event)).attr('stroke-opacity',.55))});
svg.selectAll('t.r').data(raisers).enter().append('text').attr('x',x0-6).attr('y',d=>yR(d)+4).attr('text-anchor','end').attr('font-size',11).text(d=>d);
svg.selectAll('t.e').data(E).enter().append('text').attr('x',x1).attr('y',d=>yE(d.event)+4).attr('text-anchor','middle').attr('font-size',11).attr('fill',d=>col(d.event)).text(d=>d.event);
svg.selectAll('t.l').data(listeners).enter().append('text').attr('x',x2+6).attr('y',d=>yL(d)+4).attr('font-size',11).text(d=>d);
svg.append('text').attr('x',x0-6).attr('y',12).attr('text-anchor','end').attr('font-size',10).attr('fill','#9a918a').text('RAISES');svg.append('text').attr('x',x2+6).attr('y',12).attr('font-size',10).attr('fill','#9a918a').text('LISTENS')}
// ---------------------------------------------------------------- dataflow maps
function mapsView(){let h='<div class="pane"><h2>Dataflow maps — drawn from docs/DATAFLOW.md</h2><p class="small">Each flowchart is generated from the arrows in the doc\'s own map for that system; the map text is beside it. '+D.maps.length+' maps. A node is the class or object named at the start of each arrow segment.</p></div>';
D.maps.forEach((m,i)=>{h+=`<div class="pane"><h2>${esc(m.title)} <span class="small">${m.nodes} nodes · ${m.edges} edges</span></h2><div class="two"><pre class="mermaid" id="mm${i}">${esc(m.mermaid)}</pre><pre class="raw">${esc(m.raw)}</pre></div></div>`});
setTimeout(()=>{try{mermaid.run({nodes:document.querySelectorAll('pre.mermaid')})}catch(e){document.querySelectorAll('pre.mermaid').forEach(p=>p.insertAdjacentHTML('beforebegin','<p class="small">mermaid did not load (offline?) — the flowchart source is shown instead</p>'))}},0);return h}
// ---------------------------------------------------------------- level map
function levelView(){if(!D.levels.length)return '<div class="pane"><h2>Level map</h2><p>No LevelDefinition assets found.</p></div>';let h='';
D.levels.forEach((L,i)=>{h+=`<div class="pane"><h2>${esc(L.name)} <span class="small">${esc(L.file)} · ${L.platforms.length} platforms · ${L.spawns.length} spawns · ${L.pickups.length} pickups · ${L.checkpoints.length} checkpoints · ${L.balloons.length} balloons · ${L.waters.length} water</span></h2>
<div class="legend"><span><i style="background:#8a8fa0"></i>platform (lighter = higher)</span><span><i style="background:#ff5c5c"></i>enemy spawn</span><span><i style="background:#c9a227"></i>boss spawn</span><span><i style="background:#4fe0d0"></i>pickup</span><span><i style="background:#5fd28a"></i>checkpoint</span><span><i style="background:#ffd166"></i>balloon</span><span><i style="background:#3a8fff"></i>water</span><span><i style="background:#fff"></i>player start</span></div>
<div id="lv${i}"></div><p class="small">Top: plan view, x across and z up the page. Bottom: elevation along z (the run's axis) with height y. Coloured bands use the canonical zone names below.</p>${zoneVocabulary(L)}</div>`});
setTimeout(()=>D.levels.forEach((L,i)=>drawLevel(L,'#lv'+i)),0);return h}
function zoneVocabulary(L){if(!L.zones||!L.zones.length)return '';
const zOf=o=>(o.position||o.center||o.basePosition||o.groundPosition||o.triggerPosition||[0,0,Infinity])[2];
const inZone=(o,z)=>{const v=zOf(o);return v>=z.zMin&&v<z.zMax};
const names=(items,z,format)=>items.filter(o=>inZone(o,z)).map(format).filter(Boolean);
return '<h3>Canonical prompt vocabulary</h3><p class="small">Say the bold zone name or any alias, then name the shipped object. Example: “In T4 — Warden Descent, retime Spawn_T4_Surge_2.”</p><table><tr><th>zone / bounds</th><th>aliases</th><th>enemies</th><th>routes and traversal</th></tr>'+L.zones.map(z=>{
const enemies=names(L.spawns,z,o=>{const term=L.enemyTerms[o.prefabKey]||{};return `${o.name} (${term.canonical||o.prefabKey})`});
const traversal=names([...(L.ramps||[]),...(L.waters||[]),...(L.balloons||[]),...(L.checkpoints||[]),...(L.pickups||[])],z,o=>o.name);
const prefix=z.id+'_';const routes=[...(L.projectileSequences||[]).map(o=>o.name),...(L.insightRoutes||[]).map(o=>o.routeId)].filter(n=>n&&n.startsWith(prefix));
return `<tr><td><b>${esc(z.id+' — '+z.canonical)}</b><br><span class="small">z ${z.zMin} to ${z.zMax}</span><br>${esc(z.prompt)}</td><td>${z.aliases.map(x=>`<span class="chip">${esc(x)}</span>`).join('')}</td><td>${enemies.map(x=>`<div class="mono">${esc(x)}</div>`).join('')||'<span class="small">—</span>'}</td><td>${[...routes,...traversal].map(x=>`<div class="mono">${esc(x)}</div>`).join('')||'<span class="small">—</span>'}</td></tr>`}).join('')+'</table>'}
function drawLevel(L,sel){const el=$(sel);if(!el||!window.d3)return;const P=L.platforms;if(!P.length)return;
const xs=P.flatMap(p=>[p.center[0]-p.size[0]/2,p.center[0]+p.size[0]/2]),zs=P.flatMap(p=>[p.center[2]-p.size[2]/2,p.center[2]+p.size[2]/2]),ys=P.flatMap(p=>[p.center[1]-p.size[1]/2,p.center[1]+p.size[1]/2]);
const W=el.clientWidth||1100,pad=30;const spanX=d3.max(xs)-d3.min(xs),spanZ=d3.max(zs)-d3.min(zs);const scale=Math.min((W-2*pad)/spanX,(W-2*pad)/spanZ, 10);
const H1=spanZ*scale+2*pad;const sx=x=>pad+(x-d3.min(xs))*scale,sz=z=>H1-pad-(z-d3.min(zs))*scale;const yc=d3.scaleSequential(d3.interpolateRgb('#3c3f4d','#e0e4ef')).domain([d3.min(ys),d3.max(ys)]);
const svg=d3.select(el).append('svg').attr('width',W).attr('height',H1).style('background','rgba(0,0,0,.35)').style('border-radius','10px');
const g=svg.append('g');svg.call(d3.zoom().scaleExtent([.5,6]).on('zoom',ev=>g.attr('transform',ev.transform)));
const zoneColor=d3.scaleOrdinal(d3.schemeTableau10).domain((L.zones||[]).map(z=>z.id));
(L.zones||[]).forEach(z=>{const a=Math.max(z.zMin,d3.min(zs)),b=Math.min(z.zMax,d3.max(zs));if(b<=a)return;g.append('rect').attr('x',0).attr('y',sz(b)).attr('width',W).attr('height',Math.max(1,sz(a)-sz(b))).attr('fill',zoneColor(z.id)).attr('fill-opacity',.075);g.append('text').attr('x',6).attr('y',sz(b)+12).attr('font-size',10).attr('fill',zoneColor(z.id)).text(z.id+' — '+z.canonical)});
g.selectAll('rect.p').data(P).enter().append('rect').attr('x',p=>sx(p.center[0]-p.size[0]/2)).attr('y',p=>sz(p.center[2]+p.size[2]/2)).attr('width',p=>p.size[0]*scale).attr('height',p=>p.size[2]*scale).attr('fill',p=>yc(p.center[1]+p.size[1]/2)).attr('fill-opacity',.8).attr('stroke','#111').append('title').text(p=>`${p.name}\ncentre ${p.center.join(', ')}\nsize ${p.size.join(' × ')}\ntop y ${(p.center[1]+p.size[1]/2).toFixed(2)}`);
(L.waters||[]).forEach(w=>{if(!w.center||!w.size)return;g.append('rect').attr('x',sx(w.center[0]-w.size[0]/2)).attr('y',sz(w.center[2]+w.size[2]/2)).attr('width',w.size[0]*scale).attr('height',w.size[2]*scale).attr('fill','#3a8fff').attr('fill-opacity',.6).append('title').text(w.name)});
const dot=(list,key,color,r,label)=>list.forEach(o=>{const v=o[key];if(!v)return;g.append('circle').attr('cx',sx(v[0])).attr('cy',sz(v[2])).attr('r',r).attr('fill',color).attr('stroke','#000').append('title').text(`${label} ${o.name||''} ${o.prefabKey||o.itemKey||''}\n${v.join(', ')}`)});
dot(L.spawns.filter(s=>s.isBoss!=='1'),'position','#ff5c5c',4,'spawn');dot(L.spawns.filter(s=>s.isBoss==='1'),'position','#c9a227',6,'BOSS');dot(L.pickups,'position','#4fe0d0',4,'pickup');dot(L.checkpoints,'position','#5fd28a',4,'checkpoint');dot(L.balloons||[],'position','#ffd166',5,'balloon');
if(L.playerStart)g.append('circle').attr('cx',sx(L.playerStart[0])).attr('cy',sz(L.playerStart[2])).attr('r',5).attr('fill','#fff').append('title').text('player start');
g.selectAll('text.n').data(P.filter(p=>p.size[0]*scale>40)).enter().append('text').attr('x',p=>sx(p.center[0]-p.size[0]/2)+3).attr('y',p=>sz(p.center[2]+p.size[2]/2)+10).attr('font-size',8).attr('fill','#111').text(p=>p.name);
// elevation
const H2=Math.max(160,(d3.max(ys)-d3.min(ys))*scale+2*pad);const ey=y=>H2-pad-(y-d3.min(ys))*scale;const ez=z=>pad+(z-d3.min(zs))*scale;
const s2=d3.select(el).append('svg').attr('width',W).attr('height',H2).style('background','rgba(0,0,0,.35)').style('border-radius','10px').style('margin-top','8px');
(L.zones||[]).forEach(z=>{const a=Math.max(z.zMin,d3.min(zs)),b=Math.min(z.zMax,d3.max(zs));if(b<=a)return;s2.append('rect').attr('x',ez(a)).attr('y',0).attr('width',Math.max(1,ez(b)-ez(a))).attr('height',H2).attr('fill',zoneColor(z.id)).attr('fill-opacity',.075);s2.append('text').attr('x',ez(a)+4).attr('y',12).attr('font-size',9).attr('fill',zoneColor(z.id)).text(z.id)});
s2.selectAll('rect.p').data(P).enter().append('rect').attr('class','p').attr('x',p=>ez(p.center[2]-p.size[2]/2)).attr('y',p=>ey(p.center[1]+p.size[1]/2)).attr('width',p=>p.size[2]*scale).attr('height',p=>Math.max(2,p.size[1]*scale)).attr('fill',p=>yc(p.center[1]+p.size[1]/2)).attr('fill-opacity',.7).attr('stroke','#111').append('title').text(p=>p.name);
const dot2=(list,key,color,r)=>list.forEach(o=>{const v=o[key];if(!v)return;s2.append('circle').attr('cx',ez(v[2])).attr('cy',ey(v[1])).attr('r',r).attr('fill',color).attr('stroke','#000')});
dot2(L.spawns,'position','#ff5c5c',3);dot2(L.pickups,'position','#4fe0d0',3);dot2(L.checkpoints,'position','#5fd28a',3);dot2(L.balloons||[],'position','#ffd166',4);if(L.playerStart)s2.append('circle').attr('cx',ez(L.playerStart[2])).attr('cy',ey(L.playerStart[1])).attr('r',4).attr('fill','#fff');
s2.append('text').attr('x',6).attr('y',12).attr('font-size',10).attr('fill','#9a918a').text('elevation: z →, y ↑')}
// ---------------------------------------------------------------- enemies
function enemiesView(){let h='<div class="pane"><h2>Enemy roster — from Assets/Data</h2><p class="small">Every EnemyData asset with its moveset. The timeline of each attack is wind-up (amber, the tell) → impact delay (white) → strike (red) → recovery (grey), to one scale across the roster; the parry cue fires 0.28 s before impact.</p></div>';
const maxT=Math.max(1,...D.enemies.flatMap(e=>e.attacks.map(a=>(a.windup||0)+(a.impactDelay||0)+(a.strikeDuration||0)+(a.recovery||0))));
const maxHP=Math.max(1,...D.enemies.map(e=>e.stats.maxHP||0)),maxPo=Math.max(1,...D.enemies.map(e=>e.stats.maxPosture||0));
D.enemies.forEach(e=>{const s=e.stats;h+=`<div class="pane"><h2>${esc(e.displayName)} <span class="small">${esc(e.id)} · scale ${s.scale??'?'} · souls ${s.soulValue??'?'}</span></h2><div class="two"><div>
<table><tr><td>HP</td><td><div class="bar"><i style="width:${100*(s.maxHP||0)/maxHP}%;background:#b41e2e"></i></div></td><td>${s.maxHP??'—'}</td></tr>
<tr><td>Posture</td><td><div class="bar"><i style="width:${100*(s.maxPosture||0)/maxPo}%;background:#c9a227"></i></div></td><td>${s.maxPosture??'—'} (regen ${s.postureRegen??'—'}/s, stagger ${s.staggerSeconds??'—'} s)</td></tr>
<tr><td>Aggression</td><td><div class="bar"><i style="width:${100*(s.aggression||0)}%;background:#ff7a1e"></i></div></td><td>${s.aggression??'—'}</td></tr>
<tr><td>Speed</td><td><div class="bar"><i style="width:${Math.min(100,100*(s.moveSpeed||0)/8)}%;background:#4fe0d0"></i></div></td><td>${s.moveSpeed??'—'} m/s</td></tr>
<tr><td>Ranges</td><td colspan=2>fights at <b>${s.preferredRange??'—'}</b> m · attacks from ${s.attackRange??'—'} m · aggro ${s.aggroRange??'—'} m</td></tr></table>
<h3 class="small" style="margin:10px 0 4px">Moveset (${e.entries.length} phrases)</h3><table>${e.entries.map(en=>`<tr><td>${esc(en.label)}</td><td class="small">w ${en.weight}</td><td class="small">${en.minRange}–${en.maxRange} m</td><td class="small">${en.hits.map(esc).join(' → ')}</td></tr>`).join('')}</table></div>
<div><h3 class="small" style="margin:0 0 4px">Attacks (${e.attacks.length})</h3>${e.attacks.map(a=>{const w=a.windup||0,i=a.impactDelay||0,st=a.strikeDuration||0,r=a.recovery||0,t=w+i+st+r;const pc=x=>(100*x/maxT)+'%';
return `<div class="card"><b>${esc(a.name)}</b> ${a.unblockable?'<span class="chip" style="background:#ff2f8a;color:#000">UNBLOCKABLE</span>':''} ${a.clip?`<span class="chip">${esc(a.clip)}</span>`:''}<div class="small">dmg ${a.damage} · range ${a.range} m + lunge ${a.lungeDistance} · cone ${a.coneDeg}° · ${t.toFixed(2)} s</div>
<div class="tl" title="windup ${w}s · impact ${i}s · strike ${st}s · recovery ${r}s"><i style="width:${pc(w)};background:#c9a227"></i><i style="width:${pc(i)};background:#fff"></i><i style="width:${pc(st)};background:#b41e2e"></i><i style="width:${pc(r)};background:#555"></i></div></div>`}).join('')}</div></div></div>`});return h}
// ---------------------------------------------------------------- docs / router / search
function doc(i){const d=D.docs[i];let html=render(d.text);
html=html.replace(/<pre><code class="language-mermaid">([\s\S]*?)<\/code><\/pre>/g,(m,src)=>'<pre class="mermaid">'+src+'</pre>');
setTimeout(()=>{try{mermaid.run({nodes:document.querySelectorAll('.doc pre.mermaid')})}catch(e){}},0);
return `<p class="small mono">${esc(d.path)}</p><div class="doc">${html}</div>`}
function show(v){const m=$('#main');const views={home,agents:agentsView,changes,tests,graph:graphView,events:eventsView,maps:mapsView,level:levelView,enemies:enemiesView};
if(views[v])m.innerHTML=views[v]();else if(v.startsWith('doc:'))m.innerHTML=doc(+v.slice(4));window.scrollTo(0,0)}
$('#q').addEventListener('input',e=>{const q=e.target.value.trim().toLowerCase();if(!q){show('home');return}let h='<div class="pane"><h2>Search: '+esc(q)+'</h2>';let any=false;
D.docs.forEach((d,i)=>{const lines=d.text.split('\n').map((l,n)=>[l,n]).filter(([l])=>l.toLowerCase().includes(q)).slice(0,12);if(!lines.length)return;any=true;
h+=`<h3><a href="#" onclick="document.querySelector('[data-v=\\'doc:${i}\\']').click();return false">${esc(d.title)}</a> <span class="small">${lines.length}+ lines</span></h3><ul class="small">`+lines.map(([l,n])=>`<li><span class="mono">${n+1}</span> ${esc(l).replace(new RegExp(q.replace(/[.*+?^${}()|[\]\\]/g,'\\$&'),'ig'),m=>'<span class="hit">'+m+'</span>')}</li>`).join('')+'</ul>'});
if(!any)h+='<p class="small">nothing</p>';$('#main').innerHTML=h+'</div>'});
nav();show('home');
</script></body></html>
"""


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--open", action="store_true", help="open the page in the default browser")
    ap.add_argument("--serve", action="store_true", help="serve Tools/dashboard/out on 127.0.0.1:8765 (blocks)")
    ap.add_argument("--port", type=int, default=8765)
    a = ap.parse_args()
    out, data = build()
    print(f"dashboard written: {out}  ({len(data['graph']['nodes'])} classes, {len(data['graph']['edges'])} refs, "
          f"{len(data['events'])} events, {len(data['maps'])} maps, {len(data['levels'])} levels, {len(data['enemies'])} enemies)")
    if a.serve:
        import http.server, functools
        handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(OUT_DIR))
        url = f"http://127.0.0.1:{a.port}/"
        print(f"serving {url} (Ctrl+C to stop)")
        if a.open:
            webbrowser.open(url)
        http.server.ThreadingHTTPServer(("127.0.0.1", a.port), handler).serve_forever()
    elif a.open:
        webbrowser.open(out.as_uri())


if __name__ == "__main__":
    sys.exit(main())
