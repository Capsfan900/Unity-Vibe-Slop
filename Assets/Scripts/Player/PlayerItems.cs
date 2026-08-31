using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Carries single-use pickups and spends them. FIFO: the leftmost HUD slot is the one that fires.
    /// </summary>
    public class PlayerItems : MonoBehaviour
    {
        public int capacity = 3;

        readonly List<ItemData> held = new List<ItemData>();
        public IReadOnlyList<ItemData> Held => held;
        public bool IsFull => held.Count >= capacity;
        public ItemData Current => held.Count > 0 ? held[0] : null;

        Health health;
        PlayerPosture posture;
        PlayerResources resources;
        PlayerCombat combat;
        FirstPersonMotor motor;
        readonly Collider[] buf = new Collider[48];

        void Awake()
        {
            health = GetComponent<Health>();
            posture = GetComponent<PlayerPosture>();
            resources = GetComponent<PlayerResources>();
            combat = GetComponent<PlayerCombat>();
            motor = GetComponent<FirstPersonMotor>();
        }

        void OnEnable()
        {
            GameEvents.PlayerRespawned += ClearAll;
        }

        void OnDisable()
        {
            GameEvents.PlayerRespawned -= ClearAll;
        }

        void Start() { Broadcast(); }

        void Update()
        {
            // Items are INDEPENDENT of wands: E always spends the first carried item, with no swapping
            // and no interaction with whatever wand is equipped.
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            if (InputReader.I.UseItemPressed) UseCurrent();
        }

        public bool TryPickup(ItemData item)
        {
            if (item == null || IsFull) return false;
            held.Add(item);
            GameEvents.RaiseItemPickedUp(item);
            Broadcast();
            AudioManager.Play(Sfx.ItemPickup, 0.9f);
            // Deliberately faint: ItemVfx.Collect draws the eye, and a strong tint here would wash
            // out the very streaks that communicate what was picked up.
            if (ScreenFlash.I != null) ScreenFlash.I.Flash(item.color, 0.06f, 0.18f);
            return true;
        }

        /// <summary>Spend a specific carried item (the offhand slot picks which one).</summary>
        public bool Use(ItemData item)
        {
            if (item == null || !held.Contains(item)) return false;
            if (combat != null && combat.IsStaggered) return false;
            held.Remove(item);
            Broadcast();
            GameEvents.RaiseItemUsed(item);
            AudioManager.Play(Sfx.ItemUse, 0.9f);
            Apply(item);
            return true;
        }

        public void UseCurrent()
        {
            if (held.Count == 0) { AudioManager.Play(Sfx.Click, 0.4f, 0.6f); return; }
            if (combat != null && combat.IsStaggered) return;

            var item = held[0];
            held.RemoveAt(0);
            Broadcast();
            GameEvents.RaiseItemUsed(item);
            AudioManager.Play(Sfx.ItemUse, 0.9f);
            Apply(item);
        }

        void ClearAll()
        {
            held.Clear();
            Broadcast();
        }

        void Broadcast() => GameEvents.RaiseItemsChanged(held.ToArray());

        // ---- effects ---------------------------------------------------------------------------

        void Apply(ItemData item)
        {
            switch (item.effect)
            {
                case ItemEffect.Updraft: DoUpdraft(item); break;
                case ItemEffect.SoulLantern: DoLantern(item); break;
                case ItemEffect.PhantomStep: StartCoroutine(PhantomCo(item)); break;
            }
        }

        void DoUpdraft(ItemData item)
        {
            if (motor != null) motor.Launch(item.power);
            if (CameraFX.I != null) CameraFX.I.FovKick(12f);
            if (CameraShake.I != null) CameraShake.I.Small();
            // A column of rising streaks off a ground ring: the launch now has a visible source under
            // the player instead of a full-screen colour wash.
            ItemVfx.Updraft(transform, item.color);
        }

        void DoLantern(ItemData item)
        {
            if (health != null) health.ResetFull();
            if (posture != null) posture.ResetFull();
            if (resources != null) resources.RefillFlask();
            // Motes drawn INWARD. Restoration should read as gathering, not as detonating.
            ItemVfx.Lantern(transform, item.color);
            if (ScreenFlash.I != null) ScreenFlash.I.Flash(item.color, 0.10f, 0.35f);
            AudioManager.Play(Sfx.Checkpoint, 0.8f, 1.1f);
        }

        IEnumerator PhantomCo(ItemData item)
        {
            if (health == null) yield break;
            health.Invulnerable = true;
            if (motor != null) motor.SpeedMultiplier = Mathf.Max(1f, item.power);
            if (CameraFX.I != null) CameraFX.I.ChromaticPulse(0.5f, item.duration);
            // Receding after-images shed behind the player for the whole duration: displacement,
            // not a flash. The chromatic pulse above already carries the "wrong" feeling.
            ItemVfx.PhantomStep(transform, item.color, item.duration);

            yield return new WaitForSeconds(item.duration);

            if (motor != null) motor.SpeedMultiplier = 1f;
            // do not stomp an execute's invulnerability
            if (combat == null || !combat.IsExecuting) health.Invulnerable = false;
        }
    }
}
