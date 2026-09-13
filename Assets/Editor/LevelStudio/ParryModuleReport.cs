using System;
using System.Collections.Generic;

namespace VibeGame1.EditorTools
{
    [Serializable]
    public sealed class ParryBeatFit
    {
        public int ordinal;
        public string enemyKey, objectId, failure;
        public float timeErrorSeconds, spatialErrorMetres, viewAngleDegrees, confidence;
        public bool visible, satisfied;
    }

    [Serializable]
    public sealed class ParryModuleReport
    {
        public List<ParryBeatFit> beats = new List<ParryBeatFit>();
    }

    public sealed class ParryModuleResult
    {
        public ParryModuleDefinition module = new ParryModuleDefinition();
        public ParryModuleReport report = new ParryModuleReport();
        public bool success;
    }

    [Serializable]
    public sealed class ParrySolverSettings
    {
        public float perchDistance = 12f;
        public float perchHeight = 1.5f;
        public float heavyCadenceTolerance = .08f;
    }
}
