using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The visible first-person arm. Two boxes (upper arm, forearm) solved by a stretchy two-bone IK
    /// between a fixed shoulder anchor and a wrist that is PARENTED TO THE HAND.
    ///
    /// <para>The direction of dependency is what makes this safe: the hand is a rigid child of the
    /// posed viewmodel model, and the weapon is a rigid child of the hand's grip. Nothing here can ever
    /// separate the hand from the weapon — the arm only ever draws the two bones needed to reach
    /// wherever the hand already is. That means every existing pose, every tuned timing and every
    /// coroutine in <see cref="WeaponViewmodel"/> / <see cref="OffhandViewmodel"/> keeps working
    /// untouched, and a swing automatically reads as coming from the shoulder.</para>
    ///
    /// <para>STRETCHY: the authored poses reach further from the shoulder (up to ~0.96m) than they do at
    /// rest (~0.74m). A hard IK clamp would snap the wrist short of the hand and visibly detach the arm
    /// from the weapon, so instead both bones scale up to <see cref="maxStretch"/> to span the gap. A
    /// slightly long greybox arm is invisible; a floating hand is not.</para>
    ///
    /// <para>Runs at execution order 200 so it solves AFTER the viewmodels have written their pose for
    /// the frame in their own LateUpdate — otherwise the arm trails the hand by a frame, which is
    /// exactly what makes a viewmodel look detached.</para>
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class ViewmodelArm : MonoBehaviour
    {
        [Header("Rig")]
        /// <summary>Marker inside the hand where the forearm ends. Drives the whole solve.</summary>
        public Transform wrist;
        public Transform upperArm;
        public Transform forearm;

        [Header("Shoulder (local to this transform)")]
        // Well out to the side and below the lens. Anything closer to the axis puts the near-plane cut
        // through the arm ON SCREEN; out here the cut happens far outside the frustum.
        public Vector3 shoulderLocal = new Vector3(0.22f, -0.40f, -0.10f);
        /// <summary>Which way the elbow breaks. Down and outward, like a real arm.</summary>
        public Vector3 poleLocal = new Vector3(0.6f, -1f, -0.35f);

        [Header("Bones")]
        public float upperLength = 0.42f;
        public float foreLength = 0.44f;
        public float upperThickness = 0.085f;
        public float foreThickness = 0.070f;
        public float maxStretch = 1.35f;

        /// <summary>Last solved elbow, world space. Exposed for tests and gizmos.</summary>
        public Vector3 ElbowWorld { get; private set; }
        public Vector3 ShoulderWorld { get { return transform.TransformPoint(shoulderLocal); } }
        /// <summary>True once a frame has been solved with a live wrist. Asserted by FeatureTests.</summary>
        public bool Solved { get; private set; }

        void LateUpdate()
        {
            Solve();
        }

        public void Solve()
        {
            if (wrist == null || upperArm == null || forearm == null) return;

            Vector3 shoulder = ShoulderWorld;
            Vector3 hand = wrist.position;
            Vector3 delta = hand - shoulder;
            float dist = delta.magnitude;
            if (dist < 1e-4f) return;
            Vector3 dir = delta / dist;

            float l1 = Mathf.Max(0.01f, upperLength);
            float l2 = Mathf.Max(0.01f, foreLength);
            float reach = l1 + l2;

            Vector3 elbow;
            if (dist >= reach * 0.999f)
            {
                // Beyond reach: straighten and stretch both bones so the hand is still reached exactly.
                float k = Mathf.Min(dist / reach, maxStretch);
                l1 *= k; l2 *= k;
                elbow = shoulder + dir * l1;
            }
            else
            {
                // Law of cosines. `a` is how far along the shoulder->hand axis the elbow sits,
                // `h` how far off it; the pole vector picks which way "off" means.
                float a = (dist * dist + l1 * l1 - l2 * l2) / (2f * dist);
                a = Mathf.Clamp(a, -l1, l1);
                float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
                Vector3 pole = transform.TransformDirection(poleLocal);
                Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
                if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(dir, Vector3.right);
                elbow = shoulder + dir * a + perp.normalized * h;
            }

            ElbowWorld = elbow;
            PlaceBone(upperArm, shoulder, elbow, upperThickness);
            PlaceBone(forearm, elbow, hand, foreThickness);
            Solved = true;
        }

        /// <summary>
        /// Stretch a unit cube between two world points. Bones are direct children of the rig root
        /// (uniform scale 1), so writing world position/rotation and a local scale is exact.
        /// </summary>
        static void PlaceBone(Transform bone, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = d / len;
            // A bone pointing straight up degenerates LookRotation into an identity spin; swap the
            // reference axis rather than let a raised arm pop.
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            bone.position = (a + b) * 0.5f;
            bone.rotation = Quaternion.LookRotation(dir, up);
            bone.localScale = new Vector3(thickness, thickness, len);
        }
    }
}
