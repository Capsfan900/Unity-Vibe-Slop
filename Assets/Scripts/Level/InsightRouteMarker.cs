using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Generated presentation marker for an authored <see cref="InsightRouteDef"/>. It stores the
    /// route data on the scene object solely so the level exporter can round-trip the marker; it has no
    /// Update, collider, trigger, input, or gameplay responsibility.
    /// </summary>
    public class InsightRouteMarker : MonoBehaviour
    {
        [SerializeField] InsightRouteDef definition = new InsightRouteDef();

        public InsightRouteDef Definition { get { return Copy(definition); } }
        public string RouteId { get { return definition != null ? definition.routeId : ""; } }

        /// <summary>Apply a data definition to this generated marker without retaining mutable arrays.</summary>
        public void Configure(InsightRouteDef source)
        {
            definition = Copy(source);
            transform.position = definition.markerPosition;
            transform.rotation = Quaternion.Euler(definition.markerEulerAngles);
            transform.localScale = definition.markerScale;
            gameObject.name = NameFor(definition.routeId);
        }

        /// <summary>Read the generated marker back into data, including its current world-space pose.</summary>
        public InsightRouteDef ToDefinition()
        {
            var copy = Copy(definition);
            copy.markerPosition = transform.position;
            copy.markerEulerAngles = transform.eulerAngles;
            copy.markerScale = transform.lossyScale;
            return copy;
        }

        public static InsightRouteDef Copy(InsightRouteDef source)
        {
            if (source == null) return new InsightRouteDef();
            return new InsightRouteDef
            {
                routeId = source.routeId ?? "",
                sourceSpawnerNames = source.sourceSpawnerNames != null
                    ? (string[])source.sourceSpawnerNames.Clone() : new string[0],
                markerPosition = source.markerPosition,
                markerEulerAngles = source.markerEulerAngles,
                markerScale = source.markerScale,
                markerMaterialKey = source.markerMaterialKey ?? "",
                entryCenter = source.entryCenter,
                entrySize = source.entrySize,
                rejoinCenter = source.rejoinCenter,
                rejoinSize = source.rejoinSize,
            };
        }

        public static string NameFor(string routeId)
        {
            return "Insight_" + (string.IsNullOrWhiteSpace(routeId) ? "Route" : routeId);
        }
    }
}
