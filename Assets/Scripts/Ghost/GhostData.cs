using System;

namespace VibeGame1
{
    /// <summary>
    /// Player state captured per sample, as a bitfield. Cosmetic only — the ghost uses these to pick a
    /// pose/tint, never to drive gameplay.
    /// </summary>
    [Flags]
    public enum GhostFlags
    {
        None = 0,
        Grounded = 1 << 0,
        Dashing = 1 << 1,
        Attacking = 1 << 2,
        Parrying = 1 << 3,
        Staggered = 1 << 4,
        Executing = 1 << 5,
        Drinking = 1 << 6,
        Dead = 1 << 7,
    }

    /// <summary>
    /// One recorded tick.
    ///
    /// Everything is quantised to integers on purpose: JsonUtility writes floats with full precision
    /// (<c>1.2339999</c> = 9 characters) while an integer centimetre value is 3-4. Quantising is what keeps
    /// a JSON ghost in the size range the design doc budgets for, without a binary format.
    ///
    /// There is deliberately NO timestamp field. Samples sit on an exact fixed tick, so
    /// <c>time = index / tickHz</c>. Dropping it saves ~11 characters per sample and, more importantly,
    /// makes it impossible for a recording's clock to drift out of step with its own indices.
    ///
    /// All fields are <c>int</c> rather than the narrower types the design doc's quantisation table lists.
    /// On disk it makes no difference (JSON writes digits either way) and it removes any doubt about
    /// JsonUtility's handling of narrow numeric types. The doc's bit-level packing belongs to the
    /// networked path, not to a local JSON blob.
    /// </summary>
    [Serializable]
    public struct GhostSample
    {
        public int x, y, z;   // world position, centimetres
        public int a;         // yaw, tenths of a degree (0..3600)
        public int p;         // pitch, tenths of a degree (-900..900)
        public int f;         // GhostFlags
        public int w;         // equipped weapon index

        public const float PosScale = 100f;    // metres -> centimetres
        public const float AngScale = 10f;     // degrees -> tenths

        public static GhostSample Create(UnityEngine.Vector3 pos, float yawDeg, float pitchDeg, GhostFlags flags, int weapon)
        {
            GhostSample s;
            s.x = UnityEngine.Mathf.RoundToInt(pos.x * PosScale);
            s.y = UnityEngine.Mathf.RoundToInt(pos.y * PosScale);
            s.z = UnityEngine.Mathf.RoundToInt(pos.z * PosScale);
            s.a = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Repeat(yawDeg, 360f) * AngScale);
            s.p = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp(pitchDeg, -90f, 90f) * AngScale);
            s.f = (int)flags;
            s.w = weapon;
            return s;
        }

        public UnityEngine.Vector3 Position => new UnityEngine.Vector3(x / PosScale, y / PosScale, z / PosScale);
        public float Yaw => a / AngScale;
        public float Pitch => p / AngScale;
        public GhostFlags Flags => (GhostFlags)f;
    }

    /// <summary>
    /// The blob: a full recorded run. Stored in its own file, separate from the leaderboard index.
    ///
    /// That split is deliberate — it mirrors the backend shape the design doc specifies (§6: Postgres for
    /// leaderboard rows, S3-compatible object storage for ghost blobs). Reading the leaderboard must not
    /// deserialise megabytes of samples, locally or remotely.
    /// </summary>
    [Serializable]
    public class GhostRecording
    {
        public string id = "";
        public string level = "";
        public int timeMs;
        public int deaths;
        public int tickHz = 30;
        public GhostSample[] samples = new GhostSample[0];

        public float Duration => tickHz > 0 && samples != null ? (samples.Length - 1) / (float)tickHz : 0f;
        public float TickInterval => tickHz > 0 ? 1f / tickHz : 1f / 30f;
        public bool IsValid => samples != null && samples.Length > 1 && tickHz > 0;
    }

    /// <summary>One leaderboard row. Metadata only — no samples, so the index stays small.</summary>
    [Serializable]
    public class RunEntry
    {
        public string id = "";
        public string level = "";
        public int timeMs;
        public int deaths;
        public string dateIso = "";
        /// <summary>Set by a server in future; always false for a locally recorded run.</summary>
        public bool verified;

        public float TimeSeconds => timeMs / 1000f;
    }

    /// <summary>The per-level leaderboard index file.</summary>
    [Serializable]
    public class RunIndex
    {
        public string level = "";
        public RunEntry[] entries = new RunEntry[0];
    }
}
