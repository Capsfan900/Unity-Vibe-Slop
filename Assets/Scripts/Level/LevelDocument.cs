using System;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A level as DATA outside the asset database: the same pieces a <see cref="LevelDefinition"/> holds,
    /// in a plain serialisable class that <c>JsonUtility</c> can write to disk at runtime.
    ///
    /// <para><b>Why a mirror and not the ScriptableObject.</b> A <c>LevelDefinition</c> is an asset; a
    /// player's custom level in a build is not, and <c>JsonUtility.ToJson</c> on a ScriptableObject
    /// drops nothing but also cannot be turned back into one without <c>CreateInstance</c> at runtime.
    /// So the in-game editor edits THIS, saves it under <c>persistentDataPath/levels/</c>, and the editor
    /// bridge (<c>LevelDefinitionBuilder.ImportDocument</c>) turns it into a real asset when you want it
    /// in the campaign. The def classes themselves (<see cref="PlatformDef"/> …) are reused verbatim, so
    /// the two shapes cannot drift: <see cref="CopyTo"/> / <see cref="FromDefinition"/> are field copies.</para>
    ///
    /// <para>Hard rule 4: a custom level is regenerable data, never scene state. The scene objects the
    /// editor shows are BUILT from this document by <see cref="LevelPieceFactory"/>, and every edit
    /// mutates the document first and rebuilds the piece from it.</para>
    /// </summary>
    [Serializable]
    public class LevelDocument
    {
        public string levelId = "custom";
        public string displayName = "Custom level";
        public float parTime = 120f;
        public Vector3 playerStart = new Vector3(0f, 1.2f, 0f);
        public float playerStartYaw = 0f;
        public List<PlatformDef> platforms = new List<PlatformDef>();
        public List<SpawnDef> spawns = new List<SpawnDef>();
        public List<PickupDef> pickups = new List<PickupDef>();
        public List<CheckpointDef> checkpoints = new List<CheckpointDef>();
        public List<TorchDef> torches = new List<TorchDef>();
        public List<BalloonDef> balloons = new List<BalloonDef>();
        public List<WaterDef> waters = new List<WaterDef>();

        /// <summary>A fresh level: one 16 m ground slab under the start, nothing else.</summary>
        public static LevelDocument NewDefault(string name)
        {
            var d = new LevelDocument { levelId = LevelEditorMath.SafeFileName(name), displayName = name };
            d.platforms.Add(new PlatformDef
            {
                name = "Ground", center = new Vector3(0f, -0.5f, 0f), size = new Vector3(16f, 1f, 16f),
                materialKey = "Platform", trim = true, trimMaterialKey = "NeonPink", isStatic = true
            });
            return d;
        }

        public static LevelDocument FromDefinition(LevelDefinition def)
        {
            var d = new LevelDocument
            {
                levelId = def.SafeLevelId, displayName = def.displayName, parTime = def.parTime,
                playerStart = def.playerStart, playerStartYaw = def.playerStartYaw,
            };
            if (def.platforms != null) d.platforms.AddRange(def.platforms);
            if (def.spawns != null) d.spawns.AddRange(def.spawns);
            if (def.pickups != null) d.pickups.AddRange(def.pickups);
            if (def.checkpoints != null) d.checkpoints.AddRange(def.checkpoints);
            if (def.torches != null) d.torches.AddRange(def.torches);
            if (def.balloons != null) d.balloons.AddRange(def.balloons);
            if (def.waters != null) d.waters.AddRange(def.waters);
            return d;
        }

        /// <summary>Write every piece onto a definition (arenas, pedestals, sky and kill zone are left as they are).</summary>
        public void CopyTo(LevelDefinition def)
        {
            def.levelId = levelId; def.displayName = displayName; def.parTime = parTime;
            def.playerStart = playerStart; def.playerStartYaw = playerStartYaw;
            def.platforms = platforms.ToArray(); def.spawns = spawns.ToArray(); def.pickups = pickups.ToArray();
            def.checkpoints = checkpoints.ToArray(); def.torches = torches.ToArray();
            def.balloons = balloons.ToArray(); def.waters = waters.ToArray();
        }

        public string ToJson() { return JsonUtility.ToJson(this, true); }

        public static LevelDocument FromJson(string json)
        {
            var d = JsonUtility.FromJson<LevelDocument>(json);
            if (d == null) return null;
            // JsonUtility leaves a missing list null; the editor indexes every one of them.
            d.platforms = d.platforms ?? new List<PlatformDef>(); d.spawns = d.spawns ?? new List<SpawnDef>();
            d.pickups = d.pickups ?? new List<PickupDef>(); d.checkpoints = d.checkpoints ?? new List<CheckpointDef>();
            d.torches = d.torches ?? new List<TorchDef>(); d.balloons = d.balloons ?? new List<BalloonDef>();
            d.waters = d.waters ?? new List<WaterDef>();
            return d;
        }

        public int PieceCount
        {
            get { return platforms.Count + spawns.Count + pickups.Count + checkpoints.Count + torches.Count + balloons.Count + waters.Count; }
        }
    }

    /// <summary>The kinds of piece the in-game editor can place. Order = the panel's list and the cycle order.</summary>
    public enum LevelPieceKind { Platform, WallFace, Balloon, Water, Spawn, Pickup, Checkpoint, Torch, PlayerStart }

    /// <summary>
    /// Pure maths for the in-game editor, kept free of scene objects so <c>LevelEditorTests</c> can
    /// hold it: grid snapping, the platform size ladder, and the file-name rule.
    /// </summary>
    public static class LevelEditorMath
    {
        public const float FineGrid = 0.5f;
        public const float PlatformGrid = 1f;

        /// <summary>Snap a world point to a grid; <paramref name="grid"/> ≤ 0 means free placement.</summary>
        public static Vector3 Snap(Vector3 p, float grid)
        {
            if (grid <= 0f) return p;
            return new Vector3(Mathf.Round(p.x / grid) * grid, Mathf.Round(p.y / grid) * grid, Mathf.Round(p.z / grid) * grid);
        }

        /// <summary>Grid for a kind: platforms and water on the metre, everything else on the half metre.</summary>
        public static float GridFor(LevelPieceKind kind)
        {
            return kind == LevelPieceKind.Platform || kind == LevelPieceKind.Water || kind == LevelPieceKind.WallFace
                ? PlatformGrid : FineGrid;
        }

        /// <summary>The platform footprint presets, in metres. Scroll steps through them; a stretch goes past the last.</summary>
        public static readonly float[] SizeLadder = { 2f, 4f, 6f, 8f };

        /// <summary>Next size on the ladder, then +2 m per step beyond it (clamped to 40); down goes the other way to 1.</summary>
        public static float StepSize(float current, int direction)
        {
            if (direction == 0) return current;
            if (direction > 0)
            {
                foreach (var s in SizeLadder) if (s > current + 0.001f) return s;
                return Mathf.Min(40f, current + 2f);
            }
            for (int i = SizeLadder.Length - 1; i >= 0; i--) if (SizeLadder[i] < current - 0.001f) return SizeLadder[i];
            return Mathf.Max(1f, current - 1f);
        }

        /// <summary>
        /// Snap with HYSTERESIS: the previous cell is kept until the raw point is more than
        /// <paramref name="fraction"/> of a cell PAST the boundary, so a preview never flickers between
        /// two cells while the aim sits on the line between them. Per axis; <paramref name="hasPrev"/>
        /// false or grid ≤ 0 falls back to a plain snap.
        /// </summary>
        public static Vector3 SnapWithHysteresis(Vector3 raw, Vector3 prev, bool hasPrev, float grid, float fraction)
        {
            if (grid <= 0f) return raw;
            Vector3 plain = Snap(raw, grid);
            if (!hasPrev) return plain;
            float keep = grid * (0.5f + Mathf.Clamp01(fraction));
            return new Vector3(
                Mathf.Abs(raw.x - prev.x) <= keep ? prev.x : plain.x,
                Mathf.Abs(raw.y - prev.y) <= keep ? prev.y : plain.y,
                Mathf.Abs(raw.z - prev.z) <= keep ? prev.z : plain.z);
        }

        /// <summary>
        /// The blend factor of an exponential ease with time constant <paramref name="tau"/> over one
        /// frame: 1 − e^(−dt/τ). Frame-rate independent by construction — the same ease at 20 and 240 fps
        /// (MOVEMENT-PRINCIPLES rule 8), which is why nothing in the editor lerps by a bare constant.
        /// </summary>
        public static float EaseFactor(float tau, float dt)
        {
            if (tau <= 0.0001f) return 1f;
            return 1f - Mathf.Exp(-Mathf.Max(0f, dt) / tau);
        }

        /// <summary>Yaw snapped to 90° steps.</summary>
        public static float RotateStep(float yaw, int direction)
        {
            return Mathf.Repeat(Mathf.Round(yaw / 90f) * 90f + 90f * Mathf.Sign(direction == 0 ? 1 : direction), 360f);
        }

        /// <summary>A platform's centre so that its TOP sits on the aimed point (the point is where you clicked on the floor).</summary>
        public static Vector3 CenterForTop(Vector3 topPoint, Vector3 size)
        {
            return new Vector3(topPoint.x, topPoint.y - size.y * 0.5f, topPoint.z);
        }

        /// <summary>
        /// The size step the editor applies when a piece is under the aim: from the PIECE's own size, never
        /// from the pending size. From play (2026-09-05, "the size buttons half work"): stepping from the
        /// pending 8 while aiming at a 2 m slab jumped it to 10; now 2 → 4.
        /// </summary>
        public static float StepSizeFrom(float pieceSize, int direction) { return StepSize(pieceSize, direction); }

        /// <summary>
        /// Arrow-key nudge as a world-axis grid step: the camera's forward and right are snapped to the
        /// nearest world axis so "up arrow" always moves the piece away from you along the grid, never
        /// diagonally. With <paramref name="vertical"/> the up/down keys move it along Y instead.
        /// </summary>
        public static Vector3 NudgeDelta(Vector2 arrows, Vector3 camForward, float grid, bool vertical)
        {
            if (grid <= 0f) grid = FineGrid;
            Vector3 f = new Vector3(camForward.x, 0f, camForward.z);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f = Mathf.Abs(f.x) > Mathf.Abs(f.z) ? new Vector3(Mathf.Sign(f.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(f.z));
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 d = r * Mathf.Sign(arrows.x) * (Mathf.Abs(arrows.x) > 0.5f ? 1f : 0f);
            if (vertical) d += Vector3.up * Mathf.Sign(arrows.y) * (Mathf.Abs(arrows.y) > 0.5f ? 1f : 0f);
            else d += f * Mathf.Sign(arrows.y) * (Mathf.Abs(arrows.y) > 0.5f ? 1f : 0f);
            return d * grid;
        }

        /// <summary>File-safe level name: letters, digits, dash and underscore; empty becomes "custom".</summary>
        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "custom";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            var s = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(s) ? "custom" : s;
        }
    }

    /// <summary>
    /// A bounded undo / redo stack of document snapshots (the document is JSON already, so a snapshot is
    /// a string). Pure, so <c>LevelEditorTests</c> can hold the bound and the redo-clears-on-edit rule.
    /// </summary>
    public class LevelUndoStack
    {
        readonly List<string> undo = new List<string>();
        readonly List<string> redo = new List<string>();
        readonly int depth;

        public LevelUndoStack(int depth) { this.depth = Mathf.Max(1, depth); }
        public int UndoCount { get { return undo.Count; } }
        public int RedoCount { get { return redo.Count; } }

        /// <summary>Record the state BEFORE an edit. A new edit forgets the redo branch.</summary>
        public void Push(string snapshot)
        {
            undo.Add(snapshot);
            if (undo.Count > depth) undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>Returns the snapshot to restore, or null. <paramref name="current"/> goes onto the redo side.</summary>
        public string Undo(string current)
        {
            if (undo.Count == 0) return null;
            string s = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            redo.Add(current);
            return s;
        }

        public string Redo(string current)
        {
            if (redo.Count == 0) return null;
            string s = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add(current);
            if (undo.Count > depth) undo.RemoveAt(0);
            return s;
        }

        public void Clear() { undo.Clear(); redo.Clear(); }
    }

    /// <summary>
    /// Debounces WHICH surface the aim is on. The aim ray flicks between a piece's top and the ground
    /// (or its side) around every edge, and the y-snap follows each flick; holding the previous surface
    /// height until a new one has been seen <c>frames</c> times in a row is what stops the preview
    /// hopping at edges. Pure: feed it heights, read back the one to use.
    /// </summary>
    public class AimSurfaceFilter
    {
        readonly int frames;
        float current; bool has;
        float candidate; int seen;

        public AimSurfaceFilter(int frames) { this.frames = Mathf.Max(1, frames); }

        public float Filter(float rawHeight, float tolerance)
        {
            if (!has) { current = rawHeight; has = true; candidate = rawHeight; seen = 0; return current; }
            if (Mathf.Abs(rawHeight - current) <= tolerance) { seen = 0; candidate = current; return current; }
            if (Mathf.Abs(rawHeight - candidate) <= tolerance) seen++; else { candidate = rawHeight; seen = 1; }
            if (seen >= frames) { current = candidate; seen = 0; }
            return current;
        }

        public void Reset() { has = false; seen = 0; }
    }
}
