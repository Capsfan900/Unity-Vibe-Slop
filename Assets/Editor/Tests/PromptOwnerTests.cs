using NUnit.Framework;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE STANDING PROMPT'S OWNER KEY (2026-09-06), the residual the two-channel split left behind and the
    /// user asked for by name: the slot under the crosshair has one line and seven writers — the grapple,
    /// the deathblow, the surge countdown, the wand altar, the sandbox switch, the level editor, the dev
    /// keys — and every one of them is EDGE-TRIGGERED. Before the key, a writer going quiet with "" blanked
    /// whatever another writer had standing there, and the blanked writer never re-raised, because to it
    /// nothing had changed. The player lost a live cue until they looked away and back.
    ///
    /// <para><see cref="PromptView.AcceptsStandingWrite"/> is pure, so the rule is arithmetic here rather
    /// than a squint in play mode. The play-mode half — that the view actually obeys it — is
    /// <c>Prompt_AnotherOwnersClearIsIgnored</c> in the feature suite.</para>
    /// </summary>
    public class PromptOwnerTests
    {
        [Test]
        public void ACueAlwaysTakesTheLine_LastSpeakerWins()
        {
            // Non-empty text is never refused: that is the behaviour the line has always had, and the key
            // was never meant to let one writer sit on the slot and lock everyone else out.
            Assert.IsTrue(PromptView.AcceptsStandingWrite(PromptOwner.Grapple, PromptOwner.Execute, "DEATHBLOW"));
            Assert.IsTrue(PromptView.AcceptsStandingWrite(PromptOwner.Anonymous, PromptOwner.Surge, "SURGE 3.2s"));
        }

        [Test]
        public void AClearFromAnotherOwnerIsRefused()
        {
            Assert.IsFalse(PromptView.AcceptsStandingWrite(PromptOwner.Grapple, PromptOwner.Execute, ""),
                "the deathblow going quiet must not blank a live GRAPPLE cue");
            Assert.IsFalse(PromptView.AcceptsStandingWrite(PromptOwner.Grapple, PromptOwner.Anonymous, ""),
                "an unowned clear must not blank an owned cue either");
            Assert.IsFalse(PromptView.AcceptsStandingWrite(PromptOwner.Grapple, null, ""));
        }

        [Test]
        public void TheHolderMayClearItsOwnCue()
        {
            Assert.IsTrue(PromptView.AcceptsStandingWrite(PromptOwner.Grapple, PromptOwner.Grapple, ""));
        }

        [Test]
        public void AnUnownedLineMayBeClearedByAnyone()
        {
            // Legacy writers that raise without a key own the line only while they are speaking; anyone
            // may take the slot back, which is exactly how the channel behaved before the key existed.
            Assert.IsTrue(PromptView.AcceptsStandingWrite(PromptOwner.Anonymous, PromptOwner.Execute, ""));
            Assert.IsTrue(PromptView.AcceptsStandingWrite(null, PromptOwner.Execute, ""));
        }

        [Test]
        public void EveryOwnerKeyIsDistinct()
        {
            // A duplicated key would silently let one writer clear another's cue — the bug this fixes.
            string[] keys =
            {
                PromptOwner.Execute, PromptOwner.Grapple, PromptOwner.Surge, PromptOwner.Pedestal,
                PromptOwner.Sandbox, PromptOwner.LevelEditor, PromptOwner.Debug
            };
            CollectionAssert.AllItemsAreUnique(keys);
            foreach (string k in keys)
                Assert.IsNotEmpty(k, "an empty key is the anonymous one and would be clearable by anybody");
        }
    }
}
