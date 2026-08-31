using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Central owner of Time.timeScale. Hitstop, ultimate slow-mo, pause and menus all request a
    /// scale here; the smallest active request wins, so overlapping effects never cancel each other.
    ///
    /// Requests declare whether they also slow the PLAYER. Hitstop and the ultimate's slow-mo do not:
    /// freezing the player's own momentum on every sword hit stutters the run in a game about flow,
    /// and the ultimate reads far better when the world crawls and you do not. Read
    /// <see cref="PlayerDelta"/> from player movement instead of Time.deltaTime.
    /// </summary>
    public class TimeScaleController : MonoBehaviour
    {
        public static TimeScaleController I { get; private set; }

        class Req { public int id; public float scale; public float endRealtime; public bool affectsPlayer; }

        readonly List<Req> reqs = new List<Req>();
        int nextId = 1;
        float baseFixedDelta;

        /// <summary>Time scale applied to the world (enemies, physics, VFX).</summary>
        public float WorldScale { get; private set; } = 1f;

        /// <summary>Time scale applied to the player. Ignores hitstop and slow-mo; 0 while paused.</summary>
        public float PlayerScale { get; private set; } = 1f;

        /// <summary>Delta time the first-person motor should use. Immune to hitstop / slow-mo.</summary>
        public static float PlayerDelta
        {
            get
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                return I != null ? dt * I.PlayerScale : Time.deltaTime;
            }
        }

        public bool HitStopActive { get; private set; }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            baseFixedDelta = Time.fixedDeltaTime;
        }

        void OnDestroy()
        {
            if (I == this)
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = baseFixedDelta;
            }
        }

        /// <summary>
        /// Request a time scale. Pass unscaledDuration &lt; 0 for a manual release.
        /// affectsPlayer=false leaves the player running at full speed (hitstop, slow-mo).
        /// </summary>
        public int Request(float scale, float unscaledDuration = -1f, bool affectsPlayer = true)
        {
            var r = new Req
            {
                id = nextId++,
                scale = Mathf.Clamp01(scale),
                endRealtime = unscaledDuration < 0 ? -1f : Time.unscaledTime + unscaledDuration,
                affectsPlayer = affectsPlayer
            };
            reqs.Add(r);
            Apply();
            return r.id;
        }

        public void Release(int handle)
        {
            reqs.RemoveAll(r => r.id == handle);
            Apply();
        }

        /// <summary>Freeze the world briefly on impact. Never slows the player.</summary>
        public void HitStop(float seconds, float scale = 0.02f)
        {
            if (seconds <= 0f) return;
            Request(scale, seconds, affectsPlayer: false);
        }

        void Update()
        {
            bool changed = reqs.RemoveAll(r => r.endRealtime >= 0f && Time.unscaledTime >= r.endRealtime) > 0;
            if (changed) Apply();
        }

        void Apply()
        {
            float world = 1f, player = 1f;
            bool hitstop = false;
            foreach (var r in reqs)
            {
                world = Mathf.Min(world, r.scale);
                if (r.affectsPlayer) player = Mathf.Min(player, r.scale);
                else if (r.scale <= 0.1f) hitstop = true;
            }
            WorldScale = world;
            PlayerScale = player;
            HitStopActive = hitstop;
            Time.timeScale = world;
            Time.fixedDeltaTime = baseFixedDelta * Mathf.Clamp(world, 0.05f, 1f);
        }

        public float CurrentScale => Time.timeScale;
    }
}
