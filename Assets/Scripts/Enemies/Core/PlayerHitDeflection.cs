using UnityEngine;

namespace VibeGame1
{
    /// <summary>What kind of player blow is about to land on an enemy. Executes and wand ripostes are never asked.</summary>
    public enum PlayerHitKind { Melee, ThrownBlade }

    /// <summary>
    /// An enemy-side component that may refuse a player's blow before it lands (2026-09-13, the Cinder
    /// Judge's magic shield). Implementations decide from their own visible stance; returning true means
    /// "deflected: no health, no posture reaches me", and <paramref name="recoilPosture"/> is what the
    /// player's own guard pays for swinging into it.
    /// </summary>
    public interface IPlayerHitDeflector
    {
        bool TryDeflectPlayerHit(PlayerHitKind kind, Vector3 point, Vector3 direction, out float recoilPosture);
    }

    /// <summary>
    /// The one seam between an outgoing player blow and an enemy that can deflect it. Called first by
    /// <see cref="WeaponController"/> (melee) and <see cref="ThrownBlade"/> (lodge); a deflected melee blow
    /// recoils the player through <see cref="PlayerCombat.ReceiveRecoil"/>. Deathblows (ExecuteInteractor,
    /// wand ripostes) never pass through here: an opened enemy is always finishable.
    /// </summary>
    public static class PlayerHitDeflection
    {
        public static bool TryDeflect(EnemyController enemy, PlayerHitKind kind, Vector3 point, Vector3 direction,
                                      out float recoilPosture)
        {
            recoilPosture = 0f;
            if (enemy == null || !enemy.IsAlive || enemy.IsStaggered) return false;
            var deflector = enemy.GetComponent<IPlayerHitDeflector>();
            return deflector != null && deflector.TryDeflectPlayerHit(kind, point, direction, out recoilPosture);
        }
    }
}
