using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Everything the enemy brain says to its body's *appearance*. The only seam between combat logic and art.
    ///
    /// <para><b>Timing is DATA-driven, never animation-driven.</b> Every method that animates receives its
    /// duration from the brain and must fit inside it. If a clip length is ever allowed to dictate a wind-up,
    /// the guarantee that the parry cue fires <c>cueLead</c> seconds before every impact breaks, and attacks
    /// stop being reliably parryable. An Animator-backed implementation must time-scale its clips to the
    /// duration it is given, not the other way round.</para>
    ///
    /// <para><b>The cue is the loudest event.</b> Whatever the art style, <see cref="CueFlash"/> must be an
    /// instant, high-contrast, unmistakable change — it is the parry signal, not decoration.</para>
    ///
    /// <para><b>Physics is not art.</b> Colliders, locomotion radius/height and <c>EnemyData.scale</c> come
    /// from the prefab root. An imported model is parented under the visual child and must never change the
    /// root's physical footprint.</para>
    ///
    /// The primitive implementation is <see cref="EnemyVisuals"/>. A model-based enemy supplies its own.
    /// </summary>
    public interface IEnemyPresentation
    {
        void Setup(EnemyData d);
        void SetAccent(Color emission);
        void SetPostureRatio(float r);

        /// <summary>Beat 1: the dim charge. Must last exactly <paramref name="seconds"/>.</summary>
        void Telegraph(EnemyAttackData atk, float seconds);

        /// <summary>Beat 2: the instant snap that means "parry NOW". No easing.</summary>
        void CueFlash(bool unblockable);

        /// <summary>Beat 3: the swing through.</summary>
        void Strike(float lunge, float seconds);

        void ClearTelegraph();
        void Recoil();
        void HitFlash();
        void Slump(bool on);

        /// <summary>
        /// Raise or drop the deathblow glyph on the body itself. <see cref="Slump"/> drives it for the
        /// ordinary break/recover pair; the brain calls it directly when the window closes for any other
        /// reason. An implementation with no marker may no-op, but a posture break that shows the player
        /// nothing ON THE ENEMY is the bug this exists to prevent.
        /// </summary>
        void SetDeathblowReady(bool ready);

        /// <summary>The visible breath after a combo — the player's window to act.</summary>
        void Settle(float seconds);

        void Roar();
        void Die();
    }
}
