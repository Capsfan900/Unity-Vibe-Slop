using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The lock-on dot must be INVISIBLE until a target exists. It was not: <c>LockOnMarker.Show</c>
    /// returned early when asked for the state it believed it was already in, and its belief started
    /// as "hidden" — so the very first <c>Show(false)</c> (Awake's) did nothing, and the metre-wide
    /// marker sphere stood enabled at the player's feet. From above it read as a white disc under the
    /// boots, and it went unseen for the whole project because nothing gave a reason to look down until
    /// the legs arrived (2026-09-04).
    /// </summary>
    public class LockOnMarkerTests
    {
        [Test]
        public void TheFirstHideActuallyHides()
        {
            var go = new GameObject("marker");
            try
            {
                var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                core.transform.SetParent(go.transform, false);
                var r = core.GetComponent<Renderer>();
                r.enabled = true;
                var m = go.AddComponent<LockOnMarker>();
                m.renderers = new[] { r };

                m.Show(false);   // the call Awake makes, on a component whose belief is already "hidden"
                Assert.IsFalse(r.enabled,
                    "Show(false) on a fresh marker left its renderer enabled: the belief cache short-circuited " +
                    "the first application, which is exactly the white disc under the player's feet.");

                m.Show(true);
                Assert.IsTrue(r.enabled);
                m.Show(false);
                Assert.IsFalse(r.enabled);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
