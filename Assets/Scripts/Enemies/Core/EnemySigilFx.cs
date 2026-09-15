using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// The floor shape a shipped attack's OWN data implies — never authored, never a new field. See
    /// docs/SOULS-AI-ACCURACY-SPEC-2026-09-14.md section 4. A no-contact stance (<c>range &lt;= 0</c>,
    /// e.g. Sky Verdict, DiscThrow, ShieldRaise) draws nothing here; those get a hand-charge flare
    /// instead, authored per-body (see <see cref="SeraphLancerVisuals"/>).
    /// </summary>
    public static class SigilShape
    {
        public enum Kind { None, Ring, Fan, Lane, Disc }

        public static Kind For(EnemyAttackData atk)
        {
            if (atk == null || atk.range <= 0f) return Kind.None;
            float cone = atk.coneDeg;
            if (cone >= 300f) return Kind.Ring;
            if (cone >= 90f && (atk.windup >= 0.9f || atk.unblockable)) return Kind.Fan;
            if (cone < 90f && atk.lungeDistance >= 1.5f) return Kind.Lane;
            if (atk.unblockable && atk.lungeDistance < 1.5f) return Kind.Disc;
            return Kind.None;
        }
    }

    /// <summary>
    /// The Signature Sigil: a shared floor tell for a special attack, drawn from
    /// <see cref="EnemyVisuals"/>'s base Telegraph/CueFlash/Strike/ClearTelegraph/Recoil/Slump/Die so
    /// every body — puppet or primitive — speaks it. Modelled on <see cref="CinderJudgeStormFx"/>:
    /// LineRenderers on a scene-level root (left behind by the body, not towed), URP/Unlit additive,
    /// normalised under the 1.05 bloom threshold. One instance per enemy, lazily created and reused —
    /// never more than <see cref="MaxLines"/> LineRenderers alive on one body.
    ///
    /// <para><b>Colour and the alert exception</b> live in <see cref="HueFor"/>: an unblockable attack's
    /// sigil is always the alert red <see cref="AlertRed"/>, regardless of the body's own hue, because
    /// red already means "move, do not parry" everywhere else in this game.</para>
    /// </summary>
    public sealed class EnemySigilFx
    {
        public const int MaxLines = 4;
        public const int RingSegments = 32;
        public const int FanArcSegments = 14;
        public const float GroundY = 0.04f;

        public const float MinAlpha = 0.25f;
        public const float CueAlpha = 0.6f;
        public const float FadeSeconds = 0.10f;
        public const float MaxGrowSeconds = 0.45f;
        public const float LaneWidth = 1.0f;
        public const float EndCapFlareSize = 0.3f;
        public const float EndCapFlareSeconds = 0.1f;
        const float LineWidth = 0.05f;

        public static readonly Color AlertRed = new Color(1f, 0.2f, 0.15f);

        readonly Transform root;
        readonly LineRenderer[] lines;
        readonly Material mat;
        Color hue = Color.white;
        bool visible;

        SigilShape.Kind Shape = SigilShape.Kind.None;

        public EnemySigilFx(string ownerName)
        {
            root = new GameObject("EnemySigil (" + ownerName + ")").transform;
            mat = Additive(Color.white);
            lines = new LineRenderer[MaxLines];
            for (int i = 0; i < MaxLines; i++)
                lines[i] = SlashFx.CreateLine(root, "Sigil" + i, RingSegments, LineWidth, LineWidth, false, mat);
            SetVisible(false);
        }

        public void SetVisible(bool on)
        {
            visible = on;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null) lines[i].enabled = on;
        }

        /// <summary>Start (or restart) drawing a new shape in a new hue.</summary>
        public void Begin(SigilShape.Kind shape, Color sigilHue)
        {
            Shape = shape;
            hue = sigilHue;
            SetVisible(shape != SigilShape.Kind.None);
        }

        /// <summary>
        /// Pose the sigil for this frame. <paramref name="feet"/> is the enemy's floor position;
        /// <paramref name="forwardDeg"/> the facing to draw FAN/LANE along (tracked until the cue,
        /// frozen after — RING/DISC ignore it, they are centred on the feet); <paramref name="growth"/>
        /// 0..1 is the wind-up ramp; <paramref name="alpha"/> the current fade.
        /// </summary>
        public void Draw(Vector3 feet, float forwardDeg, float range, float lunge, float coneDeg, float growth, float alpha)
        {
            if (!visible || Shape == SigilShape.Kind.None || root == null) return;
            Tint(Mathf.Clamp01(alpha));
            // A LANE widens line 0; every shape sets its own width so the next RING is not a metre thick.
            float w = Shape == SigilShape.Kind.Lane ? LaneWidth : LineWidth;
            for (int i = 0; i < lines.Length; i++) { lines[i].startWidth = w; lines[i].endWidth = w; }
            float g = Mathf.Clamp(growth, 0.3f, 1f);
            switch (Shape)
            {
                case SigilShape.Kind.Ring:
                case SigilShape.Kind.Disc:
                    DrawRing(feet, RingRadius(range) * g);
                    break;
                case SigilShape.Kind.Fan:
                    DrawFan(feet, forwardDeg, RingRadius(range) * g, coneDeg);
                    break;
                case SigilShape.Kind.Lane:
                    DrawLane(feet, forwardDeg, LaneLength(range, lunge) * g);
                    break;
            }
        }

        void DrawRing(Vector3 center, float radius)
        {
            var lr = lines[0];
            lr.loop = true;
            lr.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, GroundY, Mathf.Sin(a) * radius));
            }
            lr.enabled = true;
            for (int i = 1; i < lines.Length; i++) lines[i].enabled = false;
        }

        void DrawFan(Vector3 apex, float forwardDeg, float radius, float coneDeg)
        {
            float half = Mathf.Max(0f, coneDeg) * 0.5f;
            var arc = lines[0];
            arc.loop = false;
            arc.positionCount = FanArcSegments;
            for (int i = 0; i < FanArcSegments; i++)
            {
                float t = i / (float)(FanArcSegments - 1);
                float a = (forwardDeg - half + coneDeg * t) * Mathf.Deg2Rad;
                arc.SetPosition(i, apex + new Vector3(Mathf.Sin(a) * radius, GroundY, Mathf.Cos(a) * radius));
            }
            arc.enabled = true;

            var edgeA = lines[1];
            edgeA.loop = false;
            edgeA.positionCount = 2;
            edgeA.SetPosition(0, apex + Vector3.up * GroundY);
            edgeA.SetPosition(1, arc.GetPosition(0));
            edgeA.enabled = true;

            var edgeB = lines[2];
            edgeB.loop = false;
            edgeB.positionCount = 2;
            edgeB.SetPosition(0, apex + Vector3.up * GroundY);
            edgeB.SetPosition(1, arc.GetPosition(FanArcSegments - 1));
            edgeB.enabled = true;

            lines[3].enabled = false;
        }

        void DrawLane(Vector3 feet, float forwardDeg, float length)
        {
            var lr = lines[0];
            lr.loop = false;
            lr.positionCount = 2;
            lr.SetPosition(0, feet + Vector3.up * GroundY);
            lr.SetPosition(1, LaneEnd(feet, forwardDeg, length));
            lr.enabled = true;
            for (int i = 1; i < lines.Length; i++) lines[i].enabled = false;
        }

        /// <summary>World point of the LANE's far end — where the commit end-cap flare lands.</summary>
        public static Vector3 LaneEnd(Vector3 feet, float forwardDeg, float length)
        {
            Vector3 dir = new Vector3(Mathf.Sin(forwardDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(forwardDeg * Mathf.Deg2Rad));
            return feet + Vector3.up * GroundY + dir * length;
        }

        public void Dispose()
        {
            if (mat != null) Object.Destroy(mat);
            if (root != null) Object.Destroy(root.gameObject);
        }

        void Tint(float alpha)
        {
            var c = new Color(hue.r, hue.g, hue.b, alpha);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        static Material Additive(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "EnemySigilRuntime" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        // ---------------------------------------------------------------- pure arithmetic (tests)

        public static float RingRadius(float range) => Mathf.Max(0.05f, range + 0.5f);

        /// <summary>The LANE's length: the lunge plus the reach plus the same 0.5 m slack `allow` uses.</summary>
        public static float LaneLength(float range, float lunge) => Mathf.Max(0.1f, range + Mathf.Max(0f, lunge) + 0.5f);

        /// <summary>
        /// hue = <see cref="SlashFx.NormaliseColor"/> of the body's own emission — UNLESS the attack is
        /// unblockable, in which case it is always <see cref="AlertRed"/> regardless of body. No new
        /// colour field; red already means "move, do not parry".
        /// </summary>
        public static Color HueFor(bool unblockable, Color bodyEmission) =>
            unblockable ? AlertRed : SlashFx.NormaliseColor(bodyEmission);
    }
}
