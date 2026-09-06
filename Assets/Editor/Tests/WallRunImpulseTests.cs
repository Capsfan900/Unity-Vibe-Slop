using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The wall run's feedback package, held to its budgets. Companion to <see cref="DashImpulseTests"/>
    /// and <see cref="SlideImpulseTests"/>.
    ///
    /// <para><b>What these can and cannot prove.</b> The diagnosis was that a wall run ends six
    /// different ways and the player could not tell any of them apart — and four of those endings are
    /// the wall LETTING GO, where the right answer is the exit-grace jump. So the load-bearing tests
    /// here are the truth table of <see cref="WallRunImpulse.EndKick"/>: the endings that already own
    /// their own cue get nothing, the let-go endings sag DOWN and only down, and a lost face drifts
    /// AWAY and only away. The rest hold the catch to "translation toward the wall, no rotation" (the
    /// lean in <c>PlayerLook</c> is the rotation), hold the patter's pitch to its falling band, and
    /// prove the shipped asset carries the shipped values (rule 9) — including that the wall run's FOV
    /// hold is smaller than the slide's, so the two sustained cues never fight.</para>
    ///
    /// <para>They cannot prove that a wall run FEELS right. That is a human at the controls.</para>
    /// </summary>
    public class WallRunImpulseTests
    {
        static GameFeelSettings Shipped()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            Assert.IsNotNull(feel, "Assets/Data/GameFeel.asset is missing — run VibeGame1/3. Create Data.");
            return feel;
        }

        static readonly WallRunEnd[] AllEnds =
        {
            WallRunEnd.None, WallRunEnd.Expired, WallRunEnd.Decayed, WallRunEnd.LostWall,
            WallRunEnd.Landed, WallRunEnd.Jumped, WallRunEnd.Cancelled, WallRunEnd.Exhausted
        };

        // ---------------------------------------------------------------- shipped values (rule 9)

        [Test]
        public void TheAssetCarriesTheShippedWallRunValues()
        {
            var feel = Shipped();
            Assert.AreEqual(3.5f, feel.wallRunFovHold, 1e-4f);
            Assert.AreEqual(0.03f, feel.wallRunAttachOffset, 1e-5f);
            Assert.AreEqual(0.12f, feel.wallRunAttachTime, 1e-4f);
            Assert.AreEqual(1.6f, feel.wallRunStepDistance, 1e-4f);
            Assert.AreEqual(0.40f, feel.wallRunStepVolume, 1e-4f);
            Assert.AreEqual(1.4f, feel.wallRunDropPitch, 1e-4f);
            Assert.AreEqual(0.03f, feel.wallRunDropOffset, 1e-5f);
            Assert.AreEqual(0.15f, feel.wallRunDropTime, 1e-4f);   // == wallRunExitGrace: the sag ends with the window
            Assert.AreEqual(0.02f, feel.wallRunLostDrift, 1e-5f);
            Assert.AreEqual(44f, feel.wallRunGritRate, 1e-4f, "wallRunGritRate");
            Assert.AreEqual(3, feel.wallRunStepSparks, "wallRunStepSparks");
        }

        [Test]
        public void TheGritRate_FallsWithSpeedAndWithAge_AndNeverGoesNegative()
        {
            // Full speed, fresh run: the whole rate. The wall is loudest at the catch.
            Assert.AreEqual(44f, WallRunImpulse.GritRate(1f, 0f, 44f), 1e-4f);
            // Speed halves the rate linearly; a dead-slow run sheds nothing.
            Assert.AreEqual(22f, WallRunImpulse.GritRate(0.5f, 0f, 44f), 1e-4f);
            Assert.AreEqual(0f, WallRunImpulse.GritRate(0f, 0f, 44f), 1e-4f);
            // Age takes it down to 40% by the end of the loan — the same direction as StepPitch, so
            // the eye and the ear agree that the wall is getting heavier.
            Assert.AreEqual(44f * 0.4f, WallRunImpulse.GritRate(1f, 1f, 44f), 1e-4f);
            Assert.Greater(WallRunImpulse.GritRate(1f, 0.5f, 44f), WallRunImpulse.GritRate(1f, 1f, 44f));
            Assert.AreEqual(0f, WallRunImpulse.GritRate(-1f, 2f, 44f), 1e-4f, "clamped");
            Assert.AreEqual(0f, WallRunImpulse.GritRate(1f, 0f, 0f), 1e-4f, "disabled");
        }

        [Test]
        public void TheGritIsDustNotFire()
        {
            // Peak channel under the 1.05 bloom threshold by a wide margin, the same 0.55 as the slide's.
            Assert.LessOrEqual(WallRunFx.GritBrightness, 0.55f + 1e-4f);
            Assert.Greater(WallRunFx.GritBrightness, 0.3f, "invisible dust is not an effect");
        }

        [Test]
        public void TheWallRunFovHoldIsSmallerThanTheSlides()
        {
            // The lean already owns the wall run's sustained channel; two loud sustained cues on one
            // move read as one wobbly cue. And SlideFx and WallRunFx share CameraFX.FovHold, so the
            // quieter of the two must be the wall run or a hand-off between them would be a visible step.
            var feel = Shipped();
            Assert.Less(feel.wallRunFovHold, feel.slideFovHold,
                "the wall run's FOV hold must sit under the slide's — the lean is the loud channel");
            Assert.Greater(feel.wallRunFovHold, 0f, "but it must exist, or the lens goes silent on the wall");
        }

        [Test]
        public void TheLetGoIsAGiveNotAHit()
        {
            // The catch borrows the dash's 20 ms yank because contact is an event. The sag is
            // deliberately slower — the wall stopped holding you, it did not strike you — and that
            // difference in attack is what keeps it distinct from the exit roll kick on a jump.
            Assert.AreEqual(DashImpulse.KickAttackFraction, WallRunImpulse.KickAttackFraction, 1e-6f);
            Assert.Greater(WallRunImpulse.DropAttackFraction, WallRunImpulse.KickAttackFraction);
            Assert.AreEqual(0.30f, WallRunImpulse.DropAttackFraction, 1e-6f);
            var feel = Shipped();
            Assert.Less(WallRunImpulse.KickAttackFraction * feel.wallRunAttachTime, 2f / 60f,
                "the catch must land inside two frames at 60 Hz or it reads as drifting into the wall");
            Assert.Greater(WallRunImpulse.DropAttackFraction * feel.wallRunDropTime,
                           WallRunImpulse.KickAttackFraction * feel.wallRunAttachTime,
                "the sag's travel must take longer than the catch's");
        }

        // ---------------------------------------------------------------- the end: truth table

        [Test]
        public void EndingsThatAlreadyOwnACueGetNothingExtra()
        {
            // Jumped: PlayerLook's exit roll kick is the loudest thing that can happen and stacking on
            // it would blur it. Landed: OnLanded fires its own cue. Cancelled: the dash or stagger that
            // cancelled the run owns the frame. None: not an ending.
            var feel = Shipped();
            foreach (var why in new[] { WallRunEnd.Jumped, WallRunEnd.Landed, WallRunEnd.Cancelled, WallRunEnd.None })
            {
                ParryImpulse.Kick k;
                bool any = WallRunImpulse.EndKick(why, Vector3.right, feel.wallRunDropPitch,
                    feel.wallRunDropOffset, feel.wallRunLostDrift, out k);
                Assert.IsFalse(any, why + " must not add a camera kick");
                Assert.AreEqual(0f, k.euler.magnitude, 1e-6f, why + " leaves the kick zeroed");
                Assert.AreEqual(0f, k.offset.magnitude, 1e-6f, why + " leaves the kick zeroed");
                Assert.IsFalse(WallRunImpulse.EndsWithDrop(why));
                Assert.IsFalse(WallRunImpulse.EndsWithThud(why));
            }
        }

        [Test]
        public void TheWallLettingGoSagsDownAndOnlyDown()
        {
            // Expired, Decayed, Exhausted: the floor going out from under you. Positive euler.x is
            // DOWN in Unity, the head sinks, and there is no lateral force in a wall giving up — so
            // the wall normal must not leak into the kick at all.
            var feel = Shipped();
            foreach (var why in new[] { WallRunEnd.Expired, WallRunEnd.Decayed, WallRunEnd.Exhausted })
            {
                Assert.IsTrue(WallRunImpulse.EndsWithDrop(why));
                foreach (var n in new[] { Vector3.right, Vector3.left, new Vector3(0.7f, 0f, 0.7f), Vector3.zero })
                {
                    ParryImpulse.Kick k;
                    Assert.IsTrue(WallRunImpulse.EndKick(why, n, feel.wallRunDropPitch,
                        feel.wallRunDropOffset, feel.wallRunLostDrift, out k), why + " must sag");
                    Assert.AreEqual(feel.wallRunDropPitch, k.euler.x, 1e-5f, why + ": positive euler.x pitches the view DOWN");
                    Assert.Greater(k.euler.x, 0f);
                    Assert.AreEqual(0f, k.euler.y, 1e-6f, why + " must never yaw — a wall run is an approach");
                    Assert.AreEqual(0f, k.euler.z, 1e-6f, why + " must not roll — PlayerLook owns the roll");
                    Assert.AreEqual(-feel.wallRunDropOffset, k.offset.y, 1e-6f, why + ": the head sinks");
                    Assert.AreEqual(0f, k.offset.x, 1e-6f, why + ": no lateral force in the wall giving up");
                    Assert.AreEqual(0f, k.offset.z, 1e-6f, why + ": no fore/aft force in the wall giving up");
                }
            }
        }

        [Test]
        public void OnlyExhaustionGetsTheThud()
        {
            // Stamina ran out ON the wall is the one ending the player must learn to hear, because the
            // fix is a different line rather than a better jump.
            foreach (var why in AllEnds)
                Assert.AreEqual(why == WallRunEnd.Exhausted, WallRunImpulse.EndsWithThud(why), why.ToString());
        }

        [Test]
        public void LosingTheFaceDriftsAwayFromItAndDoesNotPitch()
        {
            // The wall did not drop you, it went away: lateral drift along the normal (AWAY from where
            // the face was), by exactly the authored metres, and no rotation of any kind.
            var feel = Shipped();
            Assert.IsFalse(WallRunImpulse.EndsWithDrop(WallRunEnd.LostWall));
            foreach (var n in new[] { Vector3.right, Vector3.left, new Vector3(0.6f, 0f, 0.8f), new Vector3(-3f, 0f, 1f) })
            {
                ParryImpulse.Kick k;
                Assert.IsTrue(WallRunImpulse.EndKick(WallRunEnd.LostWall, n, feel.wallRunDropPitch,
                    feel.wallRunDropOffset, feel.wallRunLostDrift, out k));
                Assert.AreEqual(0f, k.euler.magnitude, 1e-6f, "a lost face has no pitch, yaw or roll in it");
                Assert.AreEqual(feel.wallRunLostDrift, k.offset.magnitude, 1e-5f, "drift is the authored metres");
                Assert.Greater(Vector3.Dot(k.offset.normalized, n.normalized), 0.999f,
                    "the lens drifts ALONG the normal, away from where the wall was");
                Assert.AreEqual(0f, k.offset.y, 1e-6f, "the drift is horizontal");
            }
        }

        [Test]
        public void LosingTheFaceWithATiltedNormalStillDriftsHorizontally()
        {
            // The normal is flattened before use; a tilted normal must not put a vertical component
            // into the drift, and the flattened direction is what the drift follows.
            var feel = Shipped();
            ParryImpulse.Kick k;
            Assert.IsTrue(WallRunImpulse.EndKick(WallRunEnd.LostWall, new Vector3(1f, 0.9f, 0f),
                feel.wallRunDropPitch, feel.wallRunDropOffset, feel.wallRunLostDrift, out k));
            Assert.AreEqual(0f, k.offset.y, 1e-6f);
            Assert.AreEqual(feel.wallRunLostDrift, k.offset.x, 1e-5f);
        }

        [Test]
        public void ADegenerateLostWallProducesNoKickRatherThanAGuess()
        {
            // A zero normal (the motor zeroes WallRunNormal before OnWallRunEnded; WallRunFx caches
            // it, but a hostile caller may not) or a zero drift both mean "nothing to say".
            ParryImpulse.Kick k;
            Assert.IsFalse(WallRunImpulse.EndKick(WallRunEnd.LostWall, Vector3.zero, 1.4f, 0.03f, 0.02f, out k));
            Assert.AreEqual(0f, k.offset.magnitude, 1e-6f);
            Assert.IsFalse(WallRunImpulse.EndKick(WallRunEnd.LostWall, Vector3.up, 1.4f, 0.03f, 0.02f, out k),
                "a purely vertical normal flattens to nothing");
            Assert.IsFalse(WallRunImpulse.EndKick(WallRunEnd.LostWall, Vector3.right, 1.4f, 0.03f, 0f, out k));
            Assert.IsFalse(WallRunImpulse.EndKick(WallRunEnd.LostWall, Vector3.right, 1.4f, 0.03f, -0.02f, out k),
                "a negative drift would pull the lens INTO a wall that is not there");
            Assert.IsFalse(float.IsNaN(k.offset.x) || float.IsNaN(k.euler.x));
        }

        [Test]
        public void NoEndingEverYawsOrRolls()
        {
            // Load-bearing, as it is for the dash: a wall run is very often an approach to a swing,
            // and a yaw kick would drag the reticle off the thing you are lining up. Roll belongs to
            // PlayerLook's lean and exit kick; a second roll here would fight them.
            foreach (var why in AllEnds)
                for (float a = 0f; a < Mathf.PI * 2f; a += 0.29f)
                {
                    ParryImpulse.Kick k;
                    WallRunImpulse.EndKick(why, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), 1.4f, 0.03f, 0.02f, out k);
                    Assert.AreEqual(0f, k.euler.y, 1e-6f, why + " yawed");
                    Assert.AreEqual(0f, k.euler.z, 1e-6f, why + " rolled");
                }
        }

        // ---------------------------------------------------------------- the patter

        [Test]
        public void StepPitchFallsAsTheLoanIsCalledInAndStaysInsideItsBand()
        {
            // The ear should hear the wall getting heavier before the let-go, the same shape as the
            // motor's gravity ramp: pitch starts bright and only ever falls, and sits above 1 the whole
            // way because a wall is struck shorter and harder than a floor.
            float prev = float.MaxValue;
            for (float f = 0f; f <= 1f + 1e-6f; f += 0.05f)
            {
                float p = WallRunImpulse.StepPitch(f);
                Assert.LessOrEqual(p, prev + 1e-6f, "pitch must never rise as the run ages, at " + f);
                Assert.GreaterOrEqual(p, 1.12f - 1e-6f);
                Assert.LessOrEqual(p, 1.30f + 1e-6f);
                prev = p;
            }
            Assert.AreEqual(1.30f, WallRunImpulse.StepPitch(0f), 1e-5f, "a fresh catch is brightest");
            Assert.AreEqual(1.12f, WallRunImpulse.StepPitch(1f), 1e-5f, "the last step before expiry is heaviest");
            Assert.Greater(WallRunImpulse.StepPitch(0f), WallRunImpulse.StepPitch(1f), "the fall must be real, not flat");
            Assert.AreEqual(1.30f, WallRunImpulse.StepPitch(-0.5f), 1e-5f, "clamped below");
            Assert.AreEqual(1.12f, WallRunImpulse.StepPitch(2f), 1e-5f, "clamped above");
            Assert.Greater(WallRunImpulse.StepPitch(1f), 1f, "a wall is never struck slower than a floor");
        }

        [Test]
        public void TheWallPatterSitsUnderTheGroundedFootstep()
        {
            // Texture, not a voice: the grounded footstep ships at 0.55 and the patter must stay under
            // it, or the wall would be louder than the floor it is standing in for.
            var feel = Shipped();
            Assert.Less(feel.wallRunStepVolume, 0.55f);
            Assert.Greater(feel.wallRunStepVolume, 0f, "a silent patter removes the whole channel");
            Assert.Greater(feel.wallRunStepDistance, 0f, "a zero stride would fire a step every frame");
        }

        // ---------------------------------------------------------------- the catch

        [Test]
        public void TheCatchIsTranslationOnlyTowardTheWall()
        {
            // The lean in PlayerLook IS the rotation of a wall run; a second rotation here would fight
            // it. The catch is the body meeting the face: lens pressed AGAINST the normal, and nothing
            // else.
            var feel = Shipped();
            foreach (var n in new[] { Vector3.right, Vector3.left, new Vector3(0.7f, 0f, 0.7f), new Vector3(-0.3f, 0f, 0.95f) })
            {
                var k = WallRunImpulse.AttachKick(n, feel.wallRunAttachOffset);
                Assert.AreEqual(0f, k.euler.magnitude, 1e-6f, "the catch must not rotate the camera for normal " + n);
                Assert.AreEqual(feel.wallRunAttachOffset, k.offset.magnitude, 1e-5f, "offset magnitude is the authored metres");
                Assert.Less(Vector3.Dot(k.offset.normalized, n.normalized), -0.999f,
                    "the lens presses TOWARD the wall — opposite its normal — for normal " + n);
                Assert.AreEqual(0f, k.offset.y, 1e-6f, "a wall run is a horizontal contact; a vertical offset invents a force");
            }
        }

        [Test]
        public void TheCatchScalesWithTheAuthoredOffset()
        {
            var a = WallRunImpulse.AttachKick(Vector3.right, 0.03f);
            var b = WallRunImpulse.AttachKick(Vector3.right, 0.06f);
            Assert.AreEqual(0.03f, a.offset.magnitude, 1e-6f);
            Assert.AreEqual(0.06f, b.offset.magnitude, 1e-6f);
            Assert.AreEqual(2f, b.offset.magnitude / a.offset.magnitude, 1e-4f, "double the metres, double the press");
            Assert.AreEqual(0f, WallRunImpulse.AttachKick(Vector3.right, 0f).offset.magnitude, 1e-6f, "zero metres is no catch");
            // An un-normalised normal must not scale the press: the direction is all it contributes.
            var big = WallRunImpulse.AttachKick(Vector3.right * 9f, 0.03f);
            Assert.AreEqual(0.03f, big.offset.magnitude, 1e-6f);
        }

        [Test]
        public void ATiltedNormalIsFlattenedBeforeTheCatch()
        {
            var k = WallRunImpulse.AttachKick(new Vector3(1f, 0.8f, 0f), 0.03f);
            Assert.AreEqual(0f, k.offset.y, 1e-6f);
            Assert.AreEqual(-0.03f, k.offset.x, 1e-5f, "the flattened normal is what the press follows");
        }

        [Test]
        public void ADegenerateNormalProducesNoCatchRatherThanAGuess()
        {
            foreach (var n in new[] { Vector3.zero, Vector3.up, Vector3.down })
            {
                var k = WallRunImpulse.AttachKick(n, 0.03f);
                Assert.AreEqual(0f, k.offset.magnitude, 1e-6f, "no wall, no catch, for normal " + n);
                Assert.AreEqual(0f, k.euler.magnitude, 1e-6f);
                Assert.IsFalse(float.IsNaN(k.offset.x) || float.IsNaN(k.offset.z), "NaN in a camera transform is unrecoverable");
            }
        }

        // ---------------------------------------------------------------- force, not light

        [Test]
        public void TheWallRunPackageIsForceNotLight()
        {
            // Every lever the wall run gained is motion or sound. If someone later adds a brightness
            // field and wires it here, this test is the thing that should be re-argued, not deleted.
            var feel = Shipped();
            Assert.Greater(feel.wallRunAttachOffset, 0f, "the catch must exist");
            Assert.Greater(feel.wallRunDropPitch, 0f, "the let-go must sag DOWN, not lift");
            Assert.Greater(feel.wallRunDropOffset, 0f, "the head must sink with it");
            Assert.Greater(feel.wallRunLostDrift, 0f, "losing the face must be visible");
            Assert.Greater(feel.wallRunAttachTime, 0f);
            Assert.Greater(feel.wallRunDropTime, 0f);
            Assert.Less(feel.wallRunAttachTime, feel.wallRunDropTime,
                "the catch is an event and must be over faster than the let-go, which is a give");
        }
    }
}
