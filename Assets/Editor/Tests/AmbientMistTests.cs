using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1.Tests
{
    /// <summary>The old local emitter remains reusable, but is no longer part of the shipped player.</summary>
    public class AmbientMistTests
    {
        const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        [Test]
        public void PlayerPrefabDoesNotCarryRouteRelativeMist()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, "run VibeGame1/4. Build Prefabs before testing the shipped profile");
            Assert.IsNull(player.GetComponentInChildren<AmbientMist>(true),
                "the atmosphere is a scene-owned cloud ocean; attaching an emitter to Player makes it follow the route");
        }

        [Test]
        public void MistIsPrewarmedWorldSpaceAndOneBoundedRenderer()
        {
            int leasesBefore = DeathMist.SharedHolderCountForTests;
            var go = new GameObject("AmbientMist_Test");
            go.transform.position = Vector3.up * 10000f;   // keep the initial ground probe independent of the open scene
            AmbientMist ambient = null;
            try
            {
                ambient = go.AddComponent<AmbientMist>();
                ambient.EnsureConfigured();
                var ps = ambient.System;

                Assert.IsNotNull(ps);
                Assert.AreEqual(leasesBefore + 1, DeathMist.SharedHolderCountForTests,
                    "AmbientMist must retain the shared DeathMist material exactly once");

                var main = ps.main;
                Assert.IsTrue(main.loop);
                Assert.IsTrue(main.prewarm, "fresh loads must begin with visible mist rather than filling for 12 seconds");
                Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace,
                    "existing mist must stay in the world when the player turns or moves");
                Assert.IsFalse(main.useUnscaledTime, "ambient atmosphere freezes with the world and pause");
                Assert.AreEqual(ParticleSystemCullingMode.AlwaysSimulate, main.cullingMode);
                Assert.AreEqual(48, main.maxParticles);

                var shape = ps.shape;
                Assert.AreEqual(ParticleSystemShapeType.Box, shape.shapeType);
                Assert.AreEqual(new Vector3(0f, -1f, 28f), shape.position);
                Assert.AreEqual(new Vector3(26f, 4f, 28f), shape.scale);

                Assert.IsFalse(ps.collision.enabled);
                Assert.IsFalse(ps.lights.enabled);
                Assert.IsFalse(ps.trails.enabled);
                Assert.IsFalse(ps.trigger.enabled);

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                Assert.IsNotNull(renderer);
                Assert.AreEqual(ParticleSystemRenderMode.Billboard, renderer.renderMode);
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
                Assert.IsFalse(renderer.receiveShadows);
                Assert.AreEqual(LightProbeUsage.Off, renderer.lightProbeUsage);
                Assert.AreEqual(ReflectionProbeUsage.Off, renderer.reflectionProbeUsage);
                Assert.AreEqual(0.22f, renderer.maxParticleSize, 1e-5f,
                    "a caught sheet may never expand to fill the first-person view");
                Assert.IsTrue(renderer.sharedMaterial.IsKeywordEnabled("_FADING_ON"));
                Assert.AreEqual(1f, renderer.sharedMaterial.GetFloat("_CameraFadingEnabled"), 1e-5f);
                Assert.AreEqual(3f, renderer.sharedMaterial.GetFloat("_CameraNearFadeDistance"), 1e-5f);
                Assert.AreEqual(9f, renderer.sharedMaterial.GetFloat("_CameraFarFadeDistance"), 1e-5f);
                Vector4 fade = renderer.sharedMaterial.GetVector("_CameraFadeParams");
                Assert.AreEqual(3f, fade.x, 1e-5f);
                Assert.AreEqual(1f / 6f, fade.y, 1e-5f,
                    "mist must fade smoothly from zero at 3 m to full at 9 m");
            }
            finally
            {
                if (ambient != null) ambient.ReleaseResources();
                Object.DestroyImmediate(go);
            }

            Assert.AreEqual(leasesBefore, DeathMist.SharedHolderCountForTests,
                "destroying AmbientMist must return its shared-material lease");
        }

        [Test]
        public void GroundPosePlacesAndTiltsTheEmissionBoxOnTheSurface()
        {
            Vector3 root = new Vector3(4f, 3f, -7f);
            Quaternion rootRotation = Quaternion.Euler(0f, 37f, 0f);
            Vector3 point = new Vector3(12f, -5f, 19f);
            Vector3 normal = Quaternion.Euler(18f, 0f, -11f) * Vector3.up;

            Vector3 localCenter;
            Quaternion localRotation;
            AmbientMist.ResolveGroundPose(root, rootRotation, point, normal, 0.8f,
                out localCenter, out localRotation);

            Vector3 resolvedWorldCenter = root + rootRotation * localCenter;
            Vector3 resolvedWorldUp = rootRotation * localRotation * Vector3.up;
            Assert.Less(Vector3.Distance(point + normal.normalized * 0.8f, resolvedWorldCenter), 1e-4f,
                "ground clearance must be measured along the contacted surface normal");
            Assert.Less(Vector3.Angle(normal, resolvedWorldUp), 1e-3f,
                "the local box rotation must align its up axis to the ground normal");

            AmbientMist.ResolveGroundPose(Vector3.zero, Quaternion.Euler(0f, 90f, 0f),
                new Vector3(0f, -2f, 28f), Vector3.up, 0.8f, out localCenter, out localRotation);
            Assert.Less(Quaternion.Angle(Quaternion.identity, localRotation), 1e-3f,
                "flat ground must leave the box local rotation at identity for every player yaw");
        }
    }
}
