using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Flowing water on the ground: the fastest surface in the game, which you skate across rather than
    /// run on. The user's words: "flowing water on the ground where you can just speed boost and slide
    /// around like skating". Neon White's water, where staying on it is worth a detour.
    ///
    /// <para><b>It only TELLS the motor it is there.</b> The physics of skating (the speed floor, the
    /// missing friction, the conveyor) live in <see cref="FirstPersonMotor"/> and <see cref="TraversalMath"/>;
    /// this component's whole job is the trigger volume, refreshed every physics step through
    /// <see cref="FirstPersonMotor.TouchWater"/>. A stay-refresh with a short grace rather than
    /// Enter/Exit bookkeeping, because a CharacterController that is disabled for a teleport never sends
    /// the Exit, and a player who stayed "in water" across a warp would skate up the next staircase.</para>
    ///
    /// <para>The trigger extends <see cref="boostHeight"/> above the surface — Neon White's boost zone —
    /// so a hop along the water keeps the speed. The surface mesh has NO collider: the floor under it is
    /// what you stand on, and the whole object sits on Interactable so the NavMesh bake and the motor's
    /// wall casts never see it.</para>
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WaterVolume : MonoBehaviour
    {
        [Tooltip("Direction the water flows, world space, flattened. Zero = still water (still fast to " +
                 "skate on, just no conveyor).")]
        public Vector3 flowDirection = Vector3.forward;
        [Tooltip("Conveyor speed, m/s, added to whatever you skate at. 6 is a brisk river.")]
        public float flowSpeed = 6f;
        [Tooltip("Metres above the surface that still count as water — the boost zone.")]
        public float boostHeight = 0.35f;
        [Tooltip("Full size of the water sheet (x, thickness, z). The trigger is this plus boostHeight.")]
        public Vector3 size = new Vector3(10f, 0.04f, 10f);

        /// <summary>The conveyor velocity this frame.</summary>
        public Vector3 Flow => TraversalMath.Flow(flowDirection, flowSpeed);
        /// <summary>World Y of the surface.</summary>
        public float SurfaceY => transform.position.y + size.y * 0.5f;

        BoxCollider trigger;

        void Awake()
        {
            trigger = GetComponent<BoxCollider>();
            ApplyTrigger();
        }

        /// <summary>Size the trigger from the sheet: the sheet plus the boost zone above it. Public so the
        /// builders can call it in edit mode after writing the fields.</summary>
        public void ApplyTrigger()
        {
            if (trigger == null) trigger = GetComponent<BoxCollider>();
            if (trigger == null) return;
            trigger.isTrigger = true;
            trigger.size = new Vector3(size.x, size.y + boostHeight, size.z);
            trigger.center = new Vector3(0f, boostHeight * 0.5f, 0f);
        }

        void OnTriggerStay(Collider other)
        {
            var motor = other.GetComponentInParent<FirstPersonMotor>();
            if (motor != null) motor.TouchWater(this);
        }

        void OnTriggerEnter(Collider other)
        {
            var motor = other.GetComponentInParent<FirstPersonMotor>();
            if (motor != null) motor.TouchWater(this);
        }
    }
}
