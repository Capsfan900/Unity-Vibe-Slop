using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>Pins Hook's turret-only, real-projectile timing contract without manufacturing a parry.</summary>
    public class HookItemTests
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        GameObject player;
        GameObject targetObject;
        GameObject otherObject;
        GameObject projectileObject;
        FirstPersonMotor motor;
        PlayerItems items;
        EnemyController target;
        EnemyController other;
        Projectile projectile;
        ItemData hook;

        [SetUp]
        public void Build()
        {
            player = new GameObject("HookPlayer");
            motor = player.AddComponent<FirstPersonMotor>();
            items = player.AddComponent<PlayerItems>();
            Set(items, "motor", motor);

            targetObject = new GameObject("HookTurret");
            target = targetObject.AddComponent<EnemyController>();
            target.data = ScriptableObject.CreateInstance<EnemyData>();
            target.data.isTurret = true;

            otherObject = new GameObject("OtherTurret");
            other = otherObject.AddComponent<EnemyController>();
            other.data = ScriptableObject.CreateInstance<EnemyData>();
            other.data.isTurret = true;

            projectileObject = new GameObject("RealIncomingBolt");
            projectile = projectileObject.AddComponent<Projectile>();
            hook = ScriptableObject.CreateInstance<ItemData>();
            hook.grapplePerfectWindow = 0.13f;

            Set(items, "<GrappleTarget>k__BackingField", target);
            Set(items, "activeHookItem", hook);
            Set(items, "hookPressedAt", Time.time);
            Set(motor, "pulling", true);
        }

        [TearDown]
        public void Clean()
        {
            if (hook != null) Object.DestroyImmediate(hook);
            if (target != null && target.data != null) Object.DestroyImmediate(target.data);
            if (other != null && other.data != null) Object.DestroyImmediate(other.data);
            Object.DestroyImmediate(projectileObject);
            Object.DestroyImmediate(otherObject);
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void OnlyMatchingIncomingTurretContactInsideTheEWindowQualifies()
        {
            Assert.IsFalse(items.TryResolveHookParry(projectile, other), "another turret's bolt");

            target.data.isTurret = false;
            Assert.IsFalse(items.TryResolveHookParry(projectile, target), "a non-turret target");
            target.data.isTurret = true;

            Set(projectile, "reflected", true);
            Assert.IsFalse(items.TryResolveHookParry(projectile, target), "a return flight is not incoming contact");
            Set(projectile, "reflected", false);

            Set(items, "hookPressedAt", Time.time - hook.grapplePerfectWindow - 0.01f);
            Assert.IsFalse(items.TryResolveHookParry(projectile, target), "late E timing");
            Set(items, "hookPressedAt", Time.time);

            Set(motor, "pulling", false);
            Assert.IsFalse(items.TryResolveHookParry(projectile, target), "pull already ended");
            Set(motor, "pulling", true);

            Assert.IsTrue(items.TryResolveHookParry(projectile, target),
                "the matching real incoming bolt inside the active Hook pull must qualify");
        }

        [Test]
        public void QualifiedContactDestroysThroughHealthAndPrimesOneDashJump()
        {
            var health = targetObject.AddComponent<Health>();
            health.SetMax(100f, false);
            health.ResetFull();
            Set(target, "<Health>k__BackingField", health);

            Assert.IsTrue(items.TryResolveHookParry(projectile, target));
            items.CompleteHookPerfect(projectile, target);

            Assert.IsTrue(health.IsDead, "Hook destruction must use the ordinary Health/death path");
            Assert.IsTrue(motor.IsHookDashJumpArmed,
                "the successful projectile contact, not mere pull arrival, grants the bonus dash-jump");
        }

        static void Set(object instance, string field, object value)
        {
            var info = instance.GetType().GetField(field, Private);
            Assert.IsNotNull(info, instance.GetType().Name + "." + field + " changed");
            info.SetValue(instance, value);
        }
    }
}
