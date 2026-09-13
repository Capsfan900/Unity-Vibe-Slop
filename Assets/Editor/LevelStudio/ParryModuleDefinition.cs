using System;

namespace VibeGame1.EditorTools
{
    [Serializable]
    public sealed class ParryModuleDefinition
    {
        public string zoneId;
        public SpawnDef[] spawns = new SpawnDef[0];
        public ProjectileSequenceDef[] sequences = new ProjectileSequenceDef[0];
    }
}
