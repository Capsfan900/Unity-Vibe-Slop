using System;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    [Serializable]
    public sealed class ParryCaptureFile
    {
        public int formatVersion = 2;
        public string sceneName, startedUtc, stoppedUtc;
        public float sampleHz = PlayerTimingCapture.SampleHz, durationSeconds;
        public int droppedSamples, droppedEvents;
        public List<TraversalSample> samples = new List<TraversalSample>();
        public List<DesiredParryBeat> desiredBeats = new List<DesiredParryBeat>();
        public List<ParryTimingEvent> events = new List<ParryTimingEvent>();
    }

    [Serializable]
    public sealed class TraversalSample
    {
        public float captureSeconds, worldTime, speed;
        public Vector3 position, velocity;
        public bool grounded, sliding, wallRunning, dashing, pulling, parryPressed;
    }

    [Serializable]
    public sealed class DesiredParryBeat
    {
        public int ordinal;
        public float captureSeconds, playerSpeed;
        public Vector3 position, velocity, lookDirection;
        public string surfaceType, zoneId, splitName;
        public bool grounded, sliding, wallRunning, dashing, pulling;
    }

    [Serializable]
    public sealed class ParryTimingEvent
    {
        public string kind, boltId, shooter, spawner, data, result;
        public float captureSeconds, worldTime, predictedContactAt, playerSpeed;
        public int phraseId, phraseOrdinal;
        public Vector3 playerPosition;
        public bool playerSliding;
    }
}
