using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The sword in the air, thrown by the BLADE THROW book spell. The player-authored twin of
    /// <see cref="SentryFlare"/>: while it is recallable it is a pull point for <see cref="BladeRecall"/>,
    /// at ANY point in its flight. If it meets the world or an enemy first it lodges there and stays
    /// recallable for a few seconds. When it expires, falls into a kill zone or is recalled it is gone and
    /// the sword is back in the hand (<see cref="Ended"/>).
    ///
    /// <para>One at a time: <see cref="Active"/> is the only blade, and <see cref="IsAway"/> is what the
    /// weapon and the parry read to leave the player unarmed while it flies. Scaled time, like the bolt
    /// and the flare, so hitstop freezes it with the world. It never damages health: an enemy lodge only
    /// adds posture, exactly as a swing's contact does (WeaponController.DoHit).</para>
    /// </summary>
    public class ThrownBlade : MonoBehaviour
    {
        public static ThrownBlade Active { get; private set; }
        public static bool IsAway => Active != null;
        /// <summary>Raised once when the active blade is gone for any reason. The argument is true when a
        /// recall consumed it, false when it expired, fell or was cleared.</summary>
        public static event Action<bool> Ended;

        public const float SweepRadius = 0.15f;
        public const float WallStandoff = 0.6f;

        ItemData item;
        WeaponData weapon;
        Vector3 origin, launch, spinAxis;
        float gravity, flightSeconds, lodgeSeconds, spinRate;
        float age, lodgeAge;
        bool lodged, spent;
        Vector3 lodgeNormal;
        EnemyController lodgedEnemy;
        Transform model;
        Transform modelInstance;
        float modelLength = 1f;
        LineRenderer trail;
        Vector3[] trailBuf;
        float trailTimer, pulseTimer;
        int worldMask;
        readonly Collider[] killBuf = new Collider[8];

        public ItemData Item => item;
        public bool IsLodged => lodged;
        public EnemyController LodgedEnemy => lodgedEnemy;
        public bool Recallable => BladeMath.Recallable(spent, lodged, age, flightSeconds, lodgeAge, lodgeSeconds);
        public Vector3 PullTarget => lodgedEnemy != null
            ? transform.position + Vector3.down * 1.0f
            : BladeMath.PullTarget(transform.position, lodgeNormal, lodged, WallStandoff);

        /// <summary>Throw one. Returns null (and throws nothing) if a blade is already away.</summary>
        public static ThrownBlade Spawn(Vector3 from, Vector3 aim, WeaponData weapon, ItemData item, int worldMask)
        {
            if (IsAway || item == null) return null;
            var go = new GameObject("ThrownBlade");
            go.transform.position = from;
            var b = go.AddComponent<ThrownBlade>();
            b.item = item;
            b.weapon = weapon;
            b.origin = from;
            Vector3 dir = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.forward;
            b.launch = dir * Mathf.Max(1f, item.bladeSpeed);
            b.gravity = Mathf.Max(0f, item.bladeGravity);
            b.flightSeconds = Mathf.Max(0.1f, item.bladeFlightSeconds);
            b.lodgeSeconds = Mathf.Max(0.1f, item.bladeLodgeSeconds);
            b.spinRate = item.bladeSpinDegreesPerSecond;
            b.spinAxis = Vector3.Cross(Vector3.up, dir).sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir).normalized : Vector3.right;
            b.worldMask = worldMask;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            b.BuildVisual();
            Active = b;
            return b;
        }

        void BuildVisual()
        {
            Color hue = item.color.maxColorComponent > 0.01f ? item.color : new Color(1f, 0.6f, 0.25f);
            var pivot = new GameObject("Spin").transform;
            pivot.SetParent(transform, false);
            pivot.localScale = Vector3.one * 0.15f;
            model = pivot;
            if (weapon != null && weapon.viewmodelPrefab != null)
            {
                var m = Instantiate(weapon.viewmodelPrefab, pivot);
                // The viewmodel is authored blade-up in hand; lay it along the throw so it tumbles end over end.
                m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                m.transform.localPosition = Vector3.zero;
                m.transform.localScale = Vector3.one * Mathf.Max(0.01f, weapon.viewmodelScale * item.bladeModelScale);
                foreach (var c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);
                var renderers = m.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(m.transform.position, Vector3.one);
                foreach (var r in renderers)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    bounds.Encapsulate(r.bounds);
                }
                modelInstance = m.transform;
                modelLength = Mathf.Max(0.1f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
            }
            var mat = SlashFx.CreateAdditiveMaterial(hue);
            // The ribbon scales with the thrown model so a big blade leaves a trail you can follow at range.
            trail = SlashFx.CreateLine(transform, "Trail", 6, 0.14f * Mathf.Max(1f, item.bladeModelScale), 0.02f, false, mat);
            trail.useWorldSpace = true;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trailBuf = new Vector3[6];
            for (int i = 0; i < trailBuf.Length; i++) { trailBuf[i] = origin; trail.SetPosition(i, origin); }
        }

        void Update()
        {
            if (spent) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Color hue = item.color.maxColorComponent > 0.01f ? item.color : new Color(1f, 0.6f, 0.25f);

            if (lodged)
            {
                lodgeAge += dt;
                if (lodgedEnemy != null && !lodgedEnemy.IsAlive) { Unparent(); }
                if (lodgeAge >= lodgeSeconds) { Finish(false, true); return; }
                pulseTimer += dt;
                if (pulseTimer >= 0.5f) { pulseTimer = 0f; SlashFx.Flare(transform.position, hue, 1.2f, 0.35f); }
                return;
            }

            age += dt;
            if (age >= flightSeconds) { Finish(false, true); return; }

            Vector3 prev = transform.position;
            Vector3 next = BladeMath.Position(origin, launch, gravity, age);
            Vector3 step = next - prev;
            float dist = step.magnitude;
            if (dist > 1e-5f)
            {
                int mask = worldMask | Layers.EnemyMask;
                RaycastHit hit;
                if (Physics.SphereCast(prev, SweepRadius, step / dist, out hit, dist, mask, QueryTriggerInteraction.Ignore))
                {
                    Lodge(hit, step / dist);
                    return;
                }
            }
            transform.position = next;
            if (model != null)
            {
                model.localRotation = Quaternion.AngleAxis(BladeMath.SpinAngle(age, spinRate), Vector3.right);
                // A multi-metre blade born 0.6 m in front of the lens would fill the screen: grow it to full
                // size over the first 0.15 s, by which time it is ~3.6 m out.
                model.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(age / 0.15f));
            }

            if (FellIntoKillZone()) { Finish(false, true); return; }

            trailTimer += dt;
            trailBuf[0] = next;
            if (trailTimer >= 0.05f)
            {
                trailTimer = 0f;
                for (int i = trailBuf.Length - 1; i > 1; i--) trailBuf[i] = trailBuf[i - 1];
                trailBuf[1] = next;
            }
            for (int i = 0; i < trailBuf.Length; i++) trail.SetPosition(i, trailBuf[i]);
        }

        void Lodge(RaycastHit hit, Vector3 dir)
        {
            lodged = true;
            lodgeAge = 0f;
            lodgeNormal = hit.normal;
            transform.position = hit.point - dir * 0.1f;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            if (model != null) { model.localRotation = Quaternion.identity; model.localScale = Vector3.one; }
            // Bite with the point, not the middle: pull the model back so only its tip sinks in.
            if (modelInstance != null) modelInstance.localPosition = Vector3.back * (modelLength * 0.35f);
            if (trail != null) trail.enabled = false;
            Color hue = item.color.maxColorComponent > 0.01f ? item.color : new Color(1f, 0.6f, 0.25f);
            SlashFx.Sparks(hit.point, hit.normal, hue, 10, 6f, 70f);
            SlashFx.Flare(hit.point, hue, 1.0f, 0.18f);
            AudioManager.Play(Sfx.Hit, 0.7f, 1.2f);

            var enemy = hit.collider != null ? hit.collider.GetComponentInParent<EnemyController>() : null;
            if (enemy != null && enemy.IsAlive)
            {
                lodgedEnemy = enemy;
                transform.SetParent(enemy.transform, true);
                float posture = (weapon != null ? weapon.postureDamage : 20f) * Mathf.Max(0f, item.bladePostureMultiplier);
                if (enemy.Posture != null) enemy.Posture.Add(posture);
            }
        }

        void Unparent()
        {
            transform.SetParent(null, true);
            lodgedEnemy = null;
        }

        bool FellIntoKillZone()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, SweepRadius, killBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
                if (killBuf[i] != null && killBuf[i].GetComponentInParent<KillZone>() != null) return true;
            return false;
        }

        /// <summary>Spent by a recall: a ring and sparks where it was, and the sword is back.</summary>
        public void Consume()
        {
            if (spent) return;
            Color hue = item.color.maxColorComponent > 0.01f ? item.color : new Color(1f, 0.6f, 0.25f);
            SlashFx.Ring(transform.position, Vector3.up, hue, 1.4f, 0.3f);
            SlashFx.Sparks(transform.position, Vector3.up, hue, 12, 6f, 120f);
            Finish(true, false);
        }

        /// <summary>Gone without a recall: expired, fell, or cleared by death/respawn.</summary>
        public void Return(bool silent)
        {
            Finish(false, !silent);
        }

        void Finish(bool recalled, bool announce)
        {
            if (spent) return;
            spent = true;
            if (announce && item != null)
                SlashFx.Sparks(transform.position, Vector3.up, item.color, 8, 4f, 140f);
            if (Active == this) Active = null;
            Destroy(gameObject);
            var e = Ended;
            if (e != null) e(recalled);
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>Clear any blade without events of its own making a sound. Death, respawn, scene reset.</summary>
        public static void ClearActive()
        {
            if (Active != null) Active.Return(true);
        }
    }
}
