using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

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
        public void ConsoleRoutesTimingCommandsThroughPrimeWithoutImplicitExport()
        {
            DeveloperAccess.LockForTests();
            try
            {
                var lockedHelp = DeveloperConsole.ExecuteCommand("help");
                StringAssert.DoesNotContain("timing", lockedHelp.message);
                StringAssert.Contains("passphrase", lockedHelp.message);

                var denied = DeveloperConsole.ExecuteCommand(" timing    status ");
                StringAssert.Contains("LOCKED", denied.message);

                DeveloperAccess.UnlockForTests();
                var help = DeveloperConsole.ExecuteCommand("help");
                StringAssert.Contains("timing prime/stop/status/export/discard", help.message);

                var status = DeveloperConsole.ExecuteCommand(" timing    status ");
                StringAssert.StartsWith("TIMING CAPTURE", status.message);
                StringAssert.DoesNotContain("EXPORTED", status.message);

                var start = DeveloperConsole.ExecuteCommand("timing start");
                StringAssert.Contains("TIMING CAPTURE", start.message);
            }
            finally { DeveloperAccess.LockForTests(); }
        }

        [Test]
        public void DirectCaptureEntryPoints_AreInertWhileLocked()
        {
            DeveloperAccess.LockForTests();

            string message;
            Assert.IsFalse(PlayerTimingCapture.StartCapture(out message));
            StringAssert.Contains("REQUIRES DEVELOPER ACCESS", message);
            StringAssert.Contains("REQUIRES DEVELOPER ACCESS", PlayerTimingCapture.Status());
            StringAssert.Contains("REQUIRES DEVELOPER ACCESS", PlayerTimingCapture.StopCapture());
            StringAssert.Contains("REQUIRES DEVELOPER ACCESS", PlayerTimingCapture.Export());
            StringAssert.Contains("REQUIRES DEVELOPER ACCESS", PlayerTimingCapture.Discard());
        }

        [Test]
        public void VersionTwoCaptureRoundTripsDesiredBeatContext()
        {
            var capture = new ParryCaptureFile();
            capture.desiredBeats.Add(new DesiredParryBeat
            {
                ordinal = 1,
                captureSeconds = 2.5f,
                position = new Vector3(1f, 2f, 3f),
                velocity = Vector3.forward * 12f,
                lookDirection = Vector3.forward,
                playerSpeed = 12f,
                surfaceType = "Ramp",
                zoneId = "T0",
                splitName = "Opening",
                grounded = true,
                sliding = true
            });

            var restored = JsonUtility.FromJson<ParryCaptureFile>(JsonUtility.ToJson(capture));

            Assert.AreEqual(2, restored.formatVersion);
            Assert.AreEqual(1, restored.desiredBeats.Count);
            Assert.AreEqual("T0", restored.desiredBeats[0].zoneId);
            Assert.AreEqual("Ramp", restored.desiredBeats[0].surfaceType);
            Assert.Greater(restored.desiredBeats[0].lookDirection.sqrMagnitude, .9f);
            Assert.Greater(restored.desiredBeats[0].playerSpeed, 0f);
        }

        [Test]
        public void AtomicExportLeavesOneJsonAndNoTemporaryFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "vibegame1-parry-capture-" + System.Guid.NewGuid());
            try
            {
                string path;
                string error;
                Assert.IsTrue(PlayerTimingCapture.TryWriteAtomic(new ParryCaptureFile(), directory, out path, out error), error);
                Assert.IsTrue(File.Exists(path));
                Assert.AreEqual(1, Directory.GetFiles(directory, "*.json").Length);
                Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
