"""Measure an enemy-forge FBX WITHOUT the Unity editor: bounds, bone heads, facing, per-clip arm span
and per-clip Hips travel, all printed in UNITY axes so the numbers go straight into a
`MiniBossFactory.ModelSpec` and an `EnemyAttackData.lungeDistance`.

This is the editor-free twin of `VibeGame1 -> Probe Forge Models` (Assets/Editor/ForgeModelProbe.cs).
It exists because the MCP bridge was down the day the Argent Halberdier came in, and the pivots,
the facing and the root-motion distances all had to be MEASURED rather than guessed (AUTHORING.md
sec. 2b: a pivot comes from the skeleton, never from the bounding box).

Run it with the forge tool's own Blender-carrying venv, from the repo root:

    "C:/Users/tyler/Main Storage/ai_skelly_tool/.venv/Scripts/python.exe" Tools/measure_forge_fbx.py -- ^
        Assets/Enemies/ArgentHalberdier.fbx Assets/Enemies/ArgentHalberdier.clips.json

Lives under Tools/ and NOT under Assets/ on purpose: Unity would try to import it.

AXES. Blender imports the FBX Y-up file as Z-up (bl = (x, -z, y) of the file); Unity mirrors X on
import. So unity = (-bl.x, bl.z, -bl.y). Right arm at +x and the tail at -z on the Halberdier confirm
the mapping. `ignore_leaf_bones=False` is load-bearing: with it on, Blender drops the hands, toes and
head as "leaf bones" and every hand-span row is empty.

READ THE TRAVEL COLUMN, NOT THE SIDECAR. The manifest's `root.forward_m` is the SOURCE motion; the
tool scales it onto the rig at export (~1.3x on a 1.86 m rig). The Hips travel printed here is what
the imported clip will carry as root motion, and it is the number `lungeDistance` has to match --
`HalberdierDataTests.EveryLungeIsTheClipsOwnTravel` holds the two together.
"""
import json
import sys

import bpy


def U(v):
    """Blender world -> Unity axes."""
    return (-v.x, v.z, -v.y)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    if len(args) < 2:
        print(__doc__)
        return 2
    fbx, manifest = args[0], args[1]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx, use_anim=True, ignore_leaf_bones=False)

    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    sc = bpy.context.scene
    sc.frame_set(0)
    dg = bpy.context.evaluated_depsgraph_get()

    # ---- bounds -------------------------------------------------------------------------------
    ev = mesh.evaluated_get(dg)
    verts = [U(mesh.matrix_world @ v.co) for v in ev.data.vertices]
    xs = [v[0] for v in verts]; ys = [v[1] for v in verts]; zs = [v[2] for v in verts]
    print("BOUNDS unity  x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f  (w %.2f h %.2f d %.2f)" % (
        min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
        max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
    print("rig rotation", tuple(round(a, 3) for a in rig.rotation_euler),
          "scale", tuple(round(a, 3) for a in rig.scale),
          "mesh scale", tuple(round(a, 3) for a in mesh.scale))

    # ---- facing, from mesh slices. The forge places every bone on the drawing's z = 0 plane, so
    # the skeleton cannot say which way the body looks; the feet, the head and the extremities can.
    # A biped's feet and face sit AHEAD of the ankle/neck line: a positive zmean on those slices
    # means the model faces +Z (yaw 0 in the ModelSpec). Anything far behind at ground level is a
    # tail or a cloak.
    print("FACING slices (unity axes):")
    def slice_(pred, label):
        s = [v for v in verts if pred(v)]
        if not s:
            print("  %-30s empty" % label)
            return
        sx = [v[0] for v in s]; sy = [v[1] for v in s]; sz = [v[2] for v in s]
        print("  %-30s n=%6d  x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f  zmean %+.2f" % (
            label, len(s), min(sx), max(sx), min(sy), max(sy), min(sz), max(sz), sum(sz) / len(sz)))
    h = max(ys)
    slice_(lambda v: v[1] < 0.12, "feet (y<0.12)")
    slice_(lambda v: v[1] > h - 0.25, "head (top 0.25 m)")
    slice_(lambda v: v[1] > h - 0.10, "crown (top 0.10 m)")
    slice_(lambda v: v[0] > 0.40, "far right (x>0.40)")
    slice_(lambda v: v[0] < -0.40, "far left (x<-0.40)")
    slice_(lambda v: v[2] > 0.55, "far +z (z>0.55)")
    slice_(lambda v: v[2] < -0.45, "far -z (z<-0.45)")
    slice_(lambda v: 0.9 < v[1] < 1.2 and abs(v[0]) < 0.2, "belly band (y .9-1.2, |x|<.2)")
    slice_(lambda v: h - 0.45 < v[1] < h - 0.25 and abs(v[0]) < 0.25, "chest band (below the head)")

    # ---- skeleton -----------------------------------------------------------------------------
    print("BONES rest (unity axes, world):")
    for b in rig.data.bones:
        head = rig.matrix_world @ b.head_local
        print("  %-14s (%6.2f, %6.2f, %6.2f)" % (b.name, *U(head)))

    # ---- per clip -----------------------------------------------------------------------------
    clips = json.load(open(manifest))
    print("fps", sc.render.fps, "frame range", sc.frame_start, sc.frame_end)
    pb = rig.pose.bones
    if "LeftHand" not in pb or "RightHand" not in pb or "Hips" not in pb:
        print("  (no LeftHand/RightHand/Hips bones -- clip sampling skipped)")
        return 0
    lh, rh, hips = pb["LeftHand"], pb["RightHand"], pb["Hips"]
    print("CLIPS  name                span@25/50/75%   handY   hipsXZ travel(start->end)  hipsY min/max")
    for c in clips:
        s, e = c["start"], c["end"]
        spans = []; hy = 0.0
        for k in (0.25, 0.5, 0.75):
            sc.frame_set(int(round(s + (e - s) * k)))
            L = rig.matrix_world @ lh.head; R = rig.matrix_world @ rh.head
            spans.append((L - R).length); hy += (U(L)[1] + U(R)[1]) / 2 / 3
        sc.frame_set(s); h0 = U(rig.matrix_world @ hips.head)
        ymin = ymax = h0[1]
        for f in range(s, e + 1):
            sc.frame_set(f); hh = U(rig.matrix_world @ hips.head)
            ymin = min(ymin, hh[1]); ymax = max(ymax, hh[1])
        sc.frame_set(e); h1 = U(rig.matrix_world @ hips.head)
        dx, dz = h1[0] - h0[0], h1[2] - h0[2]
        print("  %-18s %.2f/%.2f/%.2f   %.2f    dx %+.2f dz %+.2f (%.2f m)   %.2f/%.2f" % (
            c["name"], *spans, hy, dx, dz, (dx * dx + dz * dz) ** 0.5, ymin, ymax))
    return 0


if __name__ == "__main__":
    sys.exit(main())
