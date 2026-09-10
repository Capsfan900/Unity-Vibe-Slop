using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// F2 of the bolt-timing plan (2026-09-06): <b>a bolt in flight is an incoming attack</b>.
    ///
    /// <para><see cref="EnemyController.AnyAttackIncoming"/> and <see cref="EnemyController.EarliestCueTime"/>
    /// only ever saw melee wind-ups, so on a span a missed bolt parry was scored a WHIFF (the 0.5 s mash
    /// tax) instead of a mistime (0.2 s), and <c>ParryController.ClampRecoveryToNextCue</c> was a no-op --
    /// breaking that controller's own stated invariant, "recovery can never outlast the next cue". Miss one
    /// bolt on a 1.6 s beat and the next could cue AND land while you were structurally unable to press.</para>
    ///
    /// <para>Each live <see cref="Projectile"/> reports its predicted cue and impact times here every frame
    /// and clears itself the moment it is spent, reflected or destroyed. The two helpers in
    /// <c>EnemyController</c> consult this list as well as their own; melee's 6 m <c>InThreatRange</c> is
    /// untouched, because a bolt already knows it is aimed at the player -- range is not the question, the
    /// arrival time is.</para>
    ///
    /// <para>Plain data with absolute times and no Unity objects, so <c>BoltTimingTests</c> drives it with
    /// no scene. Statics do not survive a domain reload with their world, so the list is cleared on load.</para>
    /// </summary>
    public static class BoltRegistry
    {
        struct Entry
        {
            public int id;
            public float cueTime;      // float.MaxValue once the bolt has already cued
            public float impactTime;
        }

        static readonly List<Entry> live = new List<Entry>();
        static int nextId;

        /// <summary>A registry key for one bolt. Its own counter rather than an instance id: those are
        /// <c>EntityId</c> in Unity 6.5 and the registry stays plain data a test can drive.
        /// Never 0, so a bolt destroyed before it was ever fired clears nothing.</summary>
        public static int NextId() { return ++nextId; }

        /// <summary>Bolts currently in flight toward the player. Read by tests and the harness.</summary>
        public static int Count => live.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Reset() { live.Clear(); nextId = 0; }

        /// <summary>Insert or update one bolt's forecast. <paramref name="cueTime"/> is
        /// <c>float.MaxValue</c> once its cue has already fired.</summary>
        public static void Report(int id, float cueTime, float impactTime)
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].id != id) continue;
                var e = live[i]; e.cueTime = cueTime; e.impactTime = impactTime; live[i] = e;
                return;
            }
            live.Add(new Entry { id = id, cueTime = cueTime, impactTime = impactTime });
        }

        /// <summary>A bolt that is spent, reflected or destroyed is no longer incoming.</summary>
        public static void Clear(int id)
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].id != id) continue;
                live.RemoveAt(i);
                return;
            }
        }

        /// <summary>Is any bolt due to land at or before <paramref name="limit"/> (an absolute time)?</summary>
        public static bool AnyImpactBefore(float limit)
        {
            for (int i = 0; i < live.Count; i++)
                if (live[i].impactTime <= limit) return true;
            return false;
        }

        /// <summary>
        /// Is a bolt whose one-shot cue has already fired still forecast to impact by the limit?
        /// Projectile reports <c>float.MaxValue</c> for cueTime after cueing, so this is the truthful
        /// UI-facing read rather than trying to recover a spent cue timestamp.
        /// </summary>
        public static bool AnyCuedImpactBefore(float limit)
        {
            for (int i = 0; i < live.Count; i++)
                if (live[i].cueTime == float.MaxValue && live[i].impactTime <= limit) return true;
            return false;
        }

        /// <summary>Soonest bolt cue at or before <paramref name="limit"/>, or <c>float.MaxValue</c>.</summary>
        public static float EarliestCueTime(float limit)
        {
            float best = float.MaxValue;
            for (int i = 0; i < live.Count; i++)
            {
                float t = live[i].cueTime;
                if (t <= limit && t < best) best = t;
            }
            return best;
        }
    }
}
