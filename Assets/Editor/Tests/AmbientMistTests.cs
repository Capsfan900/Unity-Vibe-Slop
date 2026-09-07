using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1.Tests
{
    /// <summary>Pins the route mist as a small presentation layer: visible on load, world-space,
    /// non-occluding and bounded tightly enough for the WebGL renderer.</summary>
    public class AmbientMistTests
    {
        const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        [Test]
        public void PlayerPrefabShipsTheApprovedAmbientMistProfile()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, "run VibeGame1/4. Build Prefabs before testing the shipped profile");

            var mist = player.GetComponent<AmbientMist>();
            Assert.IsNotNull(mist, "Player root must carry AmbientMist; camera attachment would rotate the field with view");
            Assert.IsNull(player.GetComponentInChildren<Camera>().GetComponent<AmbientMist>(),
                "mist follows the player root as an emitter, never the camera");

            Assert.AreEqual(48, mist.maxParticles);
            Assert.AreEqual(4f, mist.emissionRate, 1e-5f);
            Assert.AreEqual(8f, mist.lifetimeMin, 1e-5f);
            Assert.AreEqual(12f, mist.lifetimeMax, 1e-5f);
            Assert.AreEqual(new Vector3(0f, -1f, 28f), mist.volumeCenter);
            Assert.AreEqual(new Vector3(26f, 4f, 28f), mist.volumeSize);
            Assert.AreEqual(8f, mist.sizeMin, 1e-5f);
            Assert.AreEqual(14f, mist.sizeMax, 1e-5f);
            Assert.AreEqual(28f, mist.groundProbeForward, 1e-5f);
            Assert.AreEqual(0.25f, mist.groundProbeInterval, 1e-5f);
            Assert.AreEqual(12f, mist.groundProbeStartHeight, 1e-5f);
            Assert.AreEqual(60f, mist.groundProbeDistance, 1e-5f);
            Assert.AreEqual(0.8f, mist.groundClearance, 1e-5f);
            Assert.AreEqual(new Color(0.28f, 0.40f, 0.58f, 1f), mist.tint);
            Assert.AreEqual(0.18f, mist.alphaMin, 1e-5f);
            Assert.AreEqual(0.26f, mist.alphaMax, 1e-5f);
            Assert.AreEqual(0.22f, mist.maxScreenSize, 1e-5f);
            Assert.AreEqual(3f, mist.cameraFadeNear, 1e-5f);
            Assert.AreEqual(9f, mist.cameraFadeFar, 1e-5f);
            Assert.AreEqual(0.35f, mist.noiseStrength, 1e-5f);
            Assert.AreEqual(0.08f, mist.noiseFrequency, 1e-5f);
            Assert.AreEqual(0.04f, mist.noiseScrollSpeed, 1e-5f);
            Assert.AreEqual(0xA11CEu, mist.randomSeed);
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
        public void MistStartsBeyondLandingsAndCannotSpendTheTellBudget()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var mist = player != null ? player.GetComponent<AmbientMist>() : null;
            Assert.IsNotNull(mist, "run VibeGame1/4. Build Prefabs before testing the shipped profile");

            float nearestSpawn = mist.volumeCenter.z - mist.volumeSize.z * 0.5f;
            float lowestSpawn = mist.volumeCenter.y - mist.volumeSize.y * 0.5f;
            float highestSpawn = mist.volumeCenter.y + mist.volumeSize.y * 0.5f;
            Assert.Greater(nearestSpawn, 12f, "new mist must begin beyond every immediate landing target");
            Assert.Less(lowestSpawn, 0f, "the field needs a low ground layer, not a cloud ceiling");
            Assert.LessOrEqual(highestSpawn, 1f, "the emission volume must stay around and below the route");
            Assert.GreaterOrEqual(mist.cameraFadeNear, 3f,
                "a sheet caught by the player must vanish before it can fill the combat-distance view");
            Assert.Greater(mist.cameraFadeFar, mist.cameraFadeNear);

            Assert.LessOrEqual(mist.alphaMax, 0.26f, "one additive sheet must stay bounded beside a combat cue");
            Assert.Less(mist.alphaMax * 3f, 0.8f,
                "three coincident sheet cores must remain below the bloom threshold before soft falloff");
            Assert.LessOrEqual(mist.emissionRate * mist.lifetimeMax, mist.maxParticles,
                "steady-state population must fit the cap instead of continuously evicting live particles");
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
