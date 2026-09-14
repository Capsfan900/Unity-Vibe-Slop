using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    public class V18GrappleTests
    {
        const string PrefabPath = "Assets/Prefabs/Legendary_FlurryBrawlerV18.prefab";

        [Test]
        public void ThrowLandsOnTheAimedSpot()
        {
            Vector3 from = new Vector3(0f, 11f, 1f);
            Vector3 target = new Vector3(0f, 0f, 6f);
            const float g = 30f, down = 14f;
            Vector3 v = V18Grapple.ThrowVelocity(from, target, 0f, down, g);
            Assert.AreEqual(-down, v.y, 1e-4f);
            // integrate the drop analytically: y(t) = 11 - 14t - 15t^2 = 0
            float t = (-down + Mathf.Sqrt(down * down + 2f * g * 11f)) / g;
            Vector3 land = from + new Vector3(v.x, 0f, v.z) * t;
            Assert.AreEqual(6f, land.z, 0.01f);
            Assert.AreEqual(0f, land.x, 0.01f);
        }

        [Test]
        public void ShippedGrabIsABlueCuedGrabWithAnUnblockableSlam()
        {
            var grab = AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/BrawlerV18_Grab.asset");
            var slam = AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/BrawlerV18_GrabSlam.asset");
            Assert.IsNotNull(grab, "run VibeGame1/3. Create Data");
            Assert.IsNotNull(slam);
            Assert.IsFalse(grab.unblockable, "a Perfect parry must be able to stop the grab");
            Assert.GreaterOrEqual(grab.windup, 0.7f, "the reach is telegraphed");
            Assert.GreaterOrEqual(grab.strikeDuration, 0.9f + 0.35f + 0.3f, "the brain stays committed through lift + hold + throw");
            Assert.IsTrue(slam.unblockable);
            Assert.Greater(slam.damage, 0f);
        }

        [Test]
        public void PrefabCarriesTheGrappleOnItsOwnLiftRoot()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(go);
            var g = go.GetComponent<V18Grapple>();
            Assert.IsNotNull(g, "V18Grapple on the root");
            Assert.IsNotNull(g.grabRoot, "GrabRoot inserted above SpinRoot");
            Assert.AreEqual("GrabRoot", g.grabRoot.name);
            Assert.IsNotNull(g.slamAttack);
            Assert.LessOrEqual(g.liftHeight, 14f, "the carry must stay under a 24 m realm ceiling");
            var v = go.GetComponentInChildren<FlurryBrawlerV18Visuals>(true);
            Assert.AreSame(g.grabRoot, v.spinRoot.parent);
        }
    }
}
