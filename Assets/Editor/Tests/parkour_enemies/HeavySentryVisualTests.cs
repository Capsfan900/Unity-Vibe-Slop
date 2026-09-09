using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// A perch sentry must not regress into a stretched melee enemy. These checks pin the presentation-only
    /// factory path while its root collider, agent, shooter and projectile data remain shared and unchanged.
    /// </summary>
    public class HeavySentryVisualTests
    {
        const string Path = "Assets/Prefabs/pshooter_enemy02.prefab";
        const float Eps = 1e-4f;

        static GameObject Heavy()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null) Assert.Ignore("run 4. Build Prefabs");
            return prefab;
        }

        [Test]
        public void HeavySentryUsesItsDedicatedReliquaryBody()
        {
            var prefab = Heavy();
            var shell = Find(prefab.transform, "HeavySentryBody");
            Assert.IsNotNull(shell, "pshooter_enemy02 must select its own builder, never the generic pill path");
            Assert.IsNotNull(Find(shell, "ReliquaryCore"));
            Assert.IsNotNull(Find(shell, "NarrowWaist"));
            Assert.IsNotNull(Find(shell, "StoneFootL"));
            Assert.IsNotNull(Find(shell, "StoneFootR"));
            Assert.IsNotNull(Find(shell, "FaceRecessL"));
            Assert.IsNotNull(Find(shell, "FaceRecessC"));
            Assert.IsNotNull(Find(shell, "FaceRecessR"));
            Assert.IsNull(Find(prefab.transform, "FloatRoot"), "the pale ghost owns the floating body path");

            var core = Find(shell, "ReliquaryCore");
            Assert.Greater(core.localScale.x, core.localScale.y * 1.45f,
                "the core stays broad and mounted rather than returning to the upright pill silhouette");
        }

        [Test]
        public void HeavySentryHasNoVisibleMeleeBladeOrArm()
        {
            var prefab = Heavy();
            var visuals = prefab.GetComponentInChildren<EnemyVisuals>(true);
            Assert.IsNotNull(visuals);
            Assert.IsNull(visuals.weapon, "a ranged reliquary never carries the generic melee blade");
            Assert.IsNull(Find(prefab.transform, "Weapon"));
            Assert.IsNull(Find(prefab.transform, "UpperArm"));
            Assert.IsNull(Find(prefab.transform, "ForeArm"));
            Assert.IsNotNull(visuals.armPivot, "empty standard pivots preserve the visual/controller contract");
            Assert.IsNotNull(visuals.weaponPivot);
        }

        [Test]
        public void HeavySentryKeepsTheSharedRootCollisionAndNavigationContract()
        {
            var prefab = Heavy();
            var collider = prefab.GetComponent<CapsuleCollider>();
            var agent = prefab.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(collider);
            Assert.IsNotNull(agent);
            Assert.AreEqual(0.45f, collider.radius, Eps);
            Assert.AreEqual(2f, collider.height, Eps);
            Assert.AreEqual(1f, collider.center.y, Eps);
            Assert.AreEqual(0.45f, agent.radius, Eps);
            Assert.AreEqual(2f, agent.height, Eps);
        }

        [Test]
        public void HeavySentryStaysInsideItsPerchPresentationBudget()
        {
            var prefab = Heavy();
            var shell = Find(prefab.transform, "HeavySentryBody");
            Assert.IsNotNull(shell);
            var renderers = shell.GetComponentsInChildren<Renderer>(true);
            Assert.LessOrEqual(renderers.Length, 12,
                "this enemy can repeat on a span; silhouette facets must not become a renderer flood");

            var materials = new HashSet<Material>();
            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null) materials.Add(material);
                }
            }
            Assert.LessOrEqual(materials.Count, 3, "the reliquary reuses the existing material palette");
            Assert.IsNull(prefab.GetComponentInChildren<ParticleSystem>(true));
            Assert.IsNull(prefab.GetComponentInChildren<Light>(true));
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = Find(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
