using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Sekiro-style boss: N deathblow segments. HP reaching 0 breaks posture instead of killing;
    /// only an execute removes a segment, then the boss heals and moves to the next phase.
    /// </summary>
    public class BossController : EnemyController
    {
        public BossData Boss => data as BossData;
        public int SegmentsLeft { get; private set; }
        public int Phase { get; private set; }
        public bool Activated { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            aggroLocked = true;
        }

        protected override void Init()
        {
            base.Init();
            if (Boss == null) { Debug.LogError("[Boss] data is not a BossData", this); return; }
            Health.deathIsStagger = true;
            SegmentsLeft = Boss.segments;
            Phase = 0;
            ApplyPhase();
            Health.OnZeroHealth += OnZeroHealth;
            Health.OnChanged += (c, m) => GameEvents.RaiseBossHealthChanged(c, m, SegmentsLeft);
            Posture.OnChanged += (c, m) => GameEvents.RaiseBossPostureChanged(c, m);
        }

        void OnZeroHealth()
        {
            // HP hitting 0 does not kill the boss — it opens the deathblow window. Hold that window
            // open longer than a normal stagger so the player has time to read the banner and close in.
            Posture.Break();
            Posture.HoldStagger(5f);
        }

        public void Activate()
        {
            if (Activated) return;
            Activated = true;
            aggroLocked = false;
            GameEvents.RaiseBossStarted(this);
            GameEvents.RaiseBossHealthChanged(Health.Current, Health.Max, SegmentsLeft);
            GameEvents.RaiseBossPostureChanged(Posture.Current, Posture.Max);
            if (visuals != null) visuals.Roar();
            AudioManager.Play(Sfx.Roar, 1f, 1f, 0.03f);
        }

        void ApplyPhase()
        {
            if (Boss.phases == null || Boss.phases.Length == 0) return;
            var p = Boss.phases[Mathf.Clamp(Phase, 0, Boss.phases.Length - 1)];
            windupMult = p.windupMultiplier;
            speedMult = p.speedMultiplier;
            if (locomotion != null) locomotion.SetSpeed(data.moveSpeed * speedMult);
            if (visuals != null) visuals.SetAccent(p.accent);
        }

        protected override AttackCombo ChooseCombo(float distanceToTarget)
        {
            if (Boss == null || Boss.phases == null || Boss.phases.Length == 0) return base.ChooseCombo(distanceToTarget);
            var p = Boss.phases[Mathf.Clamp(Phase, 0, Boss.phases.Length - 1)];
            if (p.patterns == null || p.patterns.Length == 0) return base.ChooseCombo(distanceToTarget);
            return p.patterns[Random.Range(0, p.patterns.Length)];
        }

        protected override void HandleStaggerEnded()
        {
            // Missed the deathblow window: boss recovers a sliver of HP so it is not stuck at 0.
            if (Health.Current <= 0f && !Health.IsDead) Health.SetCurrent(Health.Max * 0.12f);
            base.HandleStaggerEnded();
        }

        protected override void HandleDeath()
        {
            SegmentsLeft--;
            if (SegmentsLeft <= 0)
            {
                GameEvents.RaiseBossHealthChanged(0f, Health.Max, 0);
                base.HandleDeath();
                GameEvents.RaiseBossDefeated();
                return;
            }

            Phase = Mathf.Min(Phase + 1, Boss.phases != null ? Boss.phases.Length - 1 : 0);
            Health.ResetFull();
            Posture.ResetFull();
            ApplyPhase();
            if (visuals != null) { visuals.Slump(false); visuals.Roar(); }
            SetState(State.Recover, 1.8f);
            GameEvents.RaiseBossHealthChanged(Health.Current, Health.Max, SegmentsLeft);
            AudioManager.Play(Sfx.Roar, 1f, 0.85f, 0.03f);
        }
    }
}
