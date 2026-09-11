using System.Collections.Generic;
using NUnit.Framework;

namespace VibeGame1.Tests
{
    public class PlayerTimingCaptureTests
    {
        [Test]
        public void CappedBuffer_RetainsExistingEvidenceAndRejectsOverflow()
        {
            var samples = new List<int>();
            Assert.IsTrue(PlayerTimingCapture.AppendCapped(samples, 10, 2));
            Assert.IsTrue(PlayerTimingCapture.AppendCapped(samples, 20, 2));
            Assert.IsFalse(PlayerTimingCapture.AppendCapped(samples, 30, 2));

            CollectionAssert.AreEqual(new[] { 10, 20 }, samples);
        }

        [Test]
        public void CappedBuffer_RejectsInvalidCapacityWithoutWriting()
        {
            var events = new List<string>();
            Assert.IsFalse(PlayerTimingCapture.AppendCapped(events, "cue", 0));
            Assert.IsEmpty(events);
        }

        [Test]
        public void ConsoleRoutesTimingCommandsWithoutImplicitExport()
        {
            var help = DeveloperConsole.ExecuteCommand("help");
            StringAssert.Contains("timing start/stop/status/export/discard", help.message);

            var status = DeveloperConsole.ExecuteCommand(" timing    status ");
            StringAssert.StartsWith("TIMING CAPTURE", status.message);
            StringAssert.DoesNotContain("EXPORTED", status.message);

            var start = DeveloperConsole.ExecuteCommand("timing start");
            StringAssert.Contains("TIMING CAPTURE", start.message);
        }
    }
}
