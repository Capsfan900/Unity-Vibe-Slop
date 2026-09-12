using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace VibeGame1
{
    /// <summary>
    /// Generated invisible anchor for an authored <see cref="ChallengeRouteDef"/>. It stores the
    /// route data on the scene object solely so the level exporter can round-trip the marker; it has no
    /// Update, collider, trigger, input, or gameplay responsibility.
    /// </summary>
    [MovedFrom(true, "VibeGame1", null, "InsightRouteMarker")]
    public class ChallengeRouteMarker : MonoBehaviour
    {
        [SerializeField] ChallengeRouteDef definition = new ChallengeRouteDef();

        public ChallengeRouteDef Definition { get { return Copy(definition); } }
        public string RouteId { get { return definition != null ? definition.routeId : ""; } }

        /// <summary>Apply a data definition to this generated marker without retaining mutable arrays.</summary>
        public void Configure(ChallengeRouteDef source)
        {
            definition = Copy(source);
            transform.position = definition.entryCenter;
            gameObject.name = NameFor(definition.routeId);
        }

        /// <summary>Read the generated marker back into data.</summary>
        public ChallengeRouteDef ToDefinition()
        {
            var copy = Copy(definition);
            copy.entryCenter = transform.position;
            return copy;
        }

        public static ChallengeRouteDef Copy(ChallengeRouteDef source)
        {
            if (source == null) return new ChallengeRouteDef();
            return new ChallengeRouteDef
            {
                meta = CopyMeta(source.meta),
                routeId = source.routeId ?? "",
                sourceSpawnerNames = source.sourceSpawnerNames != null
                    ? (string[])source.sourceSpawnerNames.Clone() : new string[0],
                entryCenter = source.entryCenter,
                entrySize = source.entrySize,
                rejoinCenter = source.rejoinCenter,
                rejoinSize = source.rejoinSize,
            };
        }

        static LevelObjectMeta CopyMeta(LevelObjectMeta source)
        {
            if (source == null) return new LevelObjectMeta();
            return new LevelObjectMeta
            {
                objectId = source.objectId ?? "",
                friendlyName = source.friendlyName ?? "",
                zoneIdOverride = source.zoneIdOverride ?? "",
            };
        }

        public static string NameFor(string routeId)
        {
            return "Challenge_" + (string.IsNullOrWhiteSpace(routeId) ? "Route" : routeId);
        }
    }
}
