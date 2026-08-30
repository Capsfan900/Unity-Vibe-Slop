using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Spend a full Parry Juice bar: time slows, a neon shockwave staggers and damages everything nearby.</summary>
    public class UltimateAbility : MonoBehaviour
    {
        public Material ringMaterial;
        public bool IsActive { get; private set; }

        PlayerResources resources;
        PlayerStats stats;
        PlayerCombat combat;
        readonly Collider[] buf = new Collider[32];

        void Awake()
        {
            resources = GetComponent<PlayerResources>();
            stats = GetComponent<PlayerStats>();
            combat = GetComponent<PlayerCombat>();
        }

        void Update()
        {
            if (!GameManager.IsPlaying || IsActive) return;
            if (!InputReader.I.UltimatePressed) return;
            if (!resources.JuiceFull) { AudioManager.Play(Sfx.Click, 0.4f, 0.6f); return; }
            if (combat.IsExecuting) return;
            StartCoroutine(UltCo());
        }

        IEnumerator UltCo()
        {
            IsActive = true;
            var d = GameManager.I.statsData;
            var feel = GameManager.I.feel;
            resources.ConsumeJuice();
            GameEvents.RaiseUltimateUsed();

            int handle = TimeScaleController.I.Request(d.ultSlowScale, d.ultSlowSeconds);
            if (CameraFX.I) { CameraFX.I.FovKick(feel.ultFovKick); CameraFX.I.ChromaticPulse(1f, d.ultSlowSeconds); }
            if (ScreenFlash.I) ScreenFlash.I.Flash(feel.ultFlash, 0.45f, 0.5f);
            AudioManager.Play(Sfx.Ultimate);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "UltimateRing";
            ring.transform.position = transform.position + Vector3.up * 0.5f;
            var rr = ring.GetComponent<Renderer>();
            if (ringMaterial != null) rr.sharedMaterial = ringMaterial;

            float t = 0f, grow = 0.5f;
            bool applied = false;
            while (t < grow)
            {
                t += Time.unscaledDeltaTime;
                float k = t / grow;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, d.ultRadius * 2f, 1f - (1f - k) * (1f - k));
                if (!applied && k >= 0.6f) { applied = true; ApplyBurst(d); }
                yield return null;
            }
            Destroy(ring);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, d.ultSlowSeconds - grow));
            TimeScaleController.I.Release(handle);
            IsActive = false;
        }

        void ApplyBurst(PlayerStatsData d)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, d.ultRadius, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            var seen = new System.Collections.Generic.HashSet<EnemyController>();
            float dmg = d.ultBaseDamage + d.ultDamagePerArcane * stats.Arcane;
            for (int i = 0; i < n; i++)
            {
                var e = buf[i].GetComponentInParent<EnemyController>();
                if (e == null || !e.IsAlive || seen.Contains(e)) continue;
                seen.Add(e);
                bool boss = e is BossController;
                e.Health.TakeDamage(new DamageInfo { damage = dmg, source = gameObject, point = e.transform.position, direction = (e.transform.position - transform.position).normalized });
                e.Posture.Add(e.Posture.Max * (boss ? d.ultBossPostureFraction : 1f));
                e.OnParried(0f);
            }
            TimeScaleController.I.HitStop(0.1f);
            if (CameraShake.I) CameraShake.I.Big();
            AudioManager.Play(Sfx.Execute, 0.8f, 1.3f);
        }
    }
}
