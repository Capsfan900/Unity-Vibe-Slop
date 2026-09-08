using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// One world-space cloud ocean below the authored course. The mesh never follows the player; all
    /// motion is deterministic vertex and fragment animation in <c>VibeGame1/Cloud Sea</c>, so the
    /// whole level shares one continuous rolling surface without particle churn or gameplay physics.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CloudSea : MonoBehaviour
    {
        public const float CampaignBaseY = -5f;
        public const float CampaignCenterZ = 150f;
        public const float CampaignWidth = 300f;
        public const float CampaignLength = 740f;
        public const int CampaignXSegments = 40;
        public const int CampaignZSegments = 96;

        public const float SandboxBaseY = -5f;
        public const float SandboxCenterX = 65f;
        public const float SandboxWidth = 320f;
        public const float SandboxLength = 240f;
        public const int SandboxXSegments = 64;
        public const int SandboxZSegments = 48;

        /// <summary>The shader adds three bounded waves whose absolute coefficients sum to this.</summary>
        public const float MaximumWaveCoefficient = 1.10f;

        [Header("Generated surface")]
        public Material cloudMaterial;
        public Vector2 surfaceSize = new Vector2(CampaignWidth, CampaignLength);
        [Min(2)] public int xSegments = CampaignXSegments;
        [Min(2)] public int zSegments = CampaignZSegments;
        [Min(0.1f)] public float verticalBounds = 2.5f;

        Mesh generatedMesh;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;

        public Mesh GeneratedMesh { get { return generatedMesh; } }
        public MeshRenderer Renderer { get { return meshRenderer; } }

        void OnEnable()
        {
            EnsureMesh();
        }

        void OnDestroy()
        {
            ReleaseMesh();
        }

        void OnDisable()
        {
            // The mesh is non-serialized and HideAndDontSave. Releasing it here prevents an editor
            // disable/domain-reload cycle from leaving MeshFilter pointed at an orphaned preview mesh.
            ReleaseMesh();
        }

        /// <summary>Canonical Level_01 profile. The broad margins keep every plane edge out in fog.</summary>
        public static CloudSea BuildCampaign(Transform parent, Material material)
        {
            return Build(parent, material, new Vector3(0f, CampaignBaseY, CampaignCenterZ),
                new Vector2(CampaignWidth, CampaignLength), CampaignXSegments, CampaignZSegments);
        }

        /// <summary>Sandbox profile covering the 60 m room and the movement yard out to x = 152.</summary>
        public static CloudSea BuildSandbox(Transform parent, Material material)
        {
            return Build(parent, material, new Vector3(SandboxCenterX, SandboxBaseY, 0f),
                new Vector2(SandboxWidth, SandboxLength), SandboxXSegments, SandboxZSegments);
        }

        public static CloudSea Build(Transform parent, Material material, Vector3 localCenter,
            Vector2 size, int segmentsX, int segmentsZ)
        {
            var go = new GameObject("CloudSea");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.layer = Starfield.SkyLayer; // presentation only; excluded from the Default-only NavMesh bake

            var sea = go.AddComponent<CloudSea>();
            sea.cloudMaterial = material;
            sea.surfaceSize = size;
            sea.xSegments = segmentsX;
            sea.zSegments = segmentsZ;
            sea.verticalBounds = 2.5f;
            sea.RebuildMesh();
            return sea;
        }

        /// <summary>Rebuilds the static grid once. The shader moves its vertices; no C# Update is needed.</summary>
        public void RebuildMesh()
        {
            ReleaseMesh();
            EnsureComponents();

            int sx = Mathf.Max(2, xSegments);
            int sz = Mathf.Max(2, zSegments);
            float width = Mathf.Max(1f, surfaceSize.x);
            float length = Mathf.Max(1f, surfaceSize.y);

            var vertices = new List<Vector3>((sx + 1) * (sz + 1));
            var normals = new List<Vector3>((sx + 1) * (sz + 1));
            var uvs = new List<Vector2>((sx + 1) * (sz + 1));
            var triangles = new List<int>(sx * sz * 6);

            for (int z = 0; z <= sz; z++)
            {
                float v = z / (float)sz;
                float pz = (v - 0.5f) * length;
                for (int x = 0; x <= sx; x++)
                {
                    float u = x / (float)sx;
                    vertices.Add(new Vector3((u - 0.5f) * width, 0f, pz));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(u, v));
                }
            }

            int stride = sx + 1;
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int a = z * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            generatedMesh = new Mesh
            {
                name = "CloudSeaGrid (generated)",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            generatedMesh.SetVertices(vertices);
            generatedMesh.SetNormals(normals);
            generatedMesh.SetUVs(0, uvs);
            generatedMesh.SetTriangles(triangles, 0, false);
            generatedMesh.bounds = new Bounds(Vector3.zero,
                new Vector3(width, Mathf.Max(0.1f, verticalBounds) * 2f, length));
            generatedMesh.UploadMeshData(false);

            meshFilter.sharedMesh = generatedMesh;
            ConfigureRenderer();
        }

        void EnsureMesh()
        {
            EnsureComponents();
            if (generatedMesh == null) RebuildMesh();
            else ConfigureRenderer();
        }

        void EnsureComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        void ConfigureRenderer()
        {
            meshRenderer.sharedMaterial = cloudMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = false;
        }

        void ReleaseMesh()
        {
            if (generatedMesh == null) return;
            if (meshFilter != null && meshFilter.sharedMesh == generatedMesh) meshFilter.sharedMesh = null;
            if (Application.isPlaying) Destroy(generatedMesh);
            else DestroyImmediate(generatedMesh);
            generatedMesh = null;
        }
    }
}
