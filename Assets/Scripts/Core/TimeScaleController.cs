using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Central owner of Time.timeScale. Hitstop, ultimate slow-mo, pause and menus all request a
    /// scale here; the smallest active request wins, so overlapping effects never cancel each other.
    /// </summary>
    public class TimeScaleController : MonoBehaviour
    {
        public static TimeScaleController I { get; private set; }

        class Req { public int id; public float scale; public float endRealtime; }

        readonly List<Req> reqs = new List<Req>();
        int nextId = 1;
        float baseFixedDelta;

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

        /// <summary>Request a time scale. Pass unscaledDuration &lt; 0 for a manual release.</summary>
        public int Request(float scale, float unscaledDuration = -1f)
        {
            var r = new Req
            {
                id = nextId++,
                scale = Mathf.Clamp01(scale),
                endRealtime = unscaledDuration < 0 ? -1f : Time.unscaledTime + unscaledDuration
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

        public void HitStop(float seconds, float scale = 0.02f)
        {
            if (seconds <= 0f) return;
            Request(scale, seconds);
        }

        void Update()
        {
            bool changed = reqs.RemoveAll(r => r.endRealtime >= 0f && Time.unscaledTime >= r.endRealtime) > 0;
            if (changed) Apply();
        }

        void Apply()
        {
            float s = 1f;
            foreach (var r in reqs) s = Mathf.Min(s, r.scale);
            Time.timeScale = s;
            Time.fixedDeltaTime = baseFixedDelta * Mathf.Clamp(s, 0.05f, 1f);
        }

        public float CurrentScale => Time.timeScale;
    }
}
