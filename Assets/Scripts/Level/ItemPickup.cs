using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A floating single-use item in the level. Collected by walking into it; restored on respawn
    /// so a run always starts from the same state.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        public ItemData item;
        public Transform visual;
        public float spinDegreesPerSecond = 70f;
        public float bobHeight = 0.25f;
        public float bobSpeed = 1.8f;

        Renderer[] renderers;
        Collider col;
        Light glow;
        Vector3 visualBase;
        bool collected;
        ItemGlint glint;

        void Awake()
        {
            col = GetComponent<Collider>();
            col.isTrigger = true;
            renderers = GetComponentsInChildren<Renderer>(true);
            glow = GetComponentInChildren<Light>(true);
            if (visual != null) visualBase = visual.localPosition;
            ApplyColor();

            // Orbiting motes + a slow emission breath, so a resting pickup reads as held magic
            // rather than a rotating cube. Persistent, not spawned per frame — every pickup in the
            // level runs this at once.
            if (item != null) glint = ItemVfx.AttachIdle(this, visual, item.color);
        }

        void OnEnable() { GameEvents.PlayerRespawned += Restore; }
        void OnDisable() { GameEvents.PlayerRespawned -= Restore; }

        void ApplyColor()
        {
            if (item == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_EmissionColor", item.color);
            mpb.SetColor("_BaseColor", Color.black);
            foreach (var r in renderers) if (r != null) r.SetPropertyBlock(mpb);
            if (glow != null)
            {
                var c = item.color;
                float m = Mathf.Max(c.maxColorComponent, 0.0001f);
                glow.color = c / m;
            }
        }

        void Update()
        {
            if (collected || visual == null) return;
            visual.Rotate(0f, spinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.unscaledTime * bobSpeed) * bobHeight);
        }

        void OnTriggerEnter(Collider other)
        {
            if (collected || item == null) return;
            var items = other.GetComponentInParent<PlayerItems>();
            if (items == null) return;
            if (!items.TryPickup(item)) return;   // inventory full — leave it for later

            // The charge visibly travels from the pedestal into the player. Fired before hiding the
            // body so the streaks start from where the item actually was.
            var cam = Camera.main;
            ItemVfx.Collect(visual != null ? visual.position : transform.position,
                            cam != null ? cam.transform : other.transform,
                            item.color);
            SetCollected(true);
        }

        void Restore() => SetCollected(false);

        void SetCollected(bool value)
        {
            collected = value;
            if (col != null) col.enabled = !value;
            foreach (var r in renderers) if (r != null) r.enabled = !value;
            if (glow != null) glow.enabled = !value;
            if (glint != null) glint.SetActive(!value);   // orbiting motes go with the body
        }

        void OnDrawGizmos()
        {
            Gizmos.color = item != null ? item.color : Color.white;
            Gizmos.DrawWireSphere(transform.position, 1f);
        }
    }
}
