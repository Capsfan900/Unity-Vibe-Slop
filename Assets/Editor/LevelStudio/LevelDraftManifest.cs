using System;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>Stable provenance and save times for one protected Level Studio working copy.</summary>
    [Serializable]
    public sealed class LevelDraftManifest
    {
        public int formatVersion = 1;
        public int revision;
        public string schema;
        public string payloadSha256;
        public string basePayloadSha256;
        public string draftId;
        public string displayName;
        public string sourceGuid;
        public string sourceLevelId;
        public string sourceFingerprint;
        public string createdUtc;
        public string savedUtc;
        public string autosavedUtc;
    }
}
