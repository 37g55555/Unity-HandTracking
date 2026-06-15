using System.Collections.Generic;
using UnityEngine;

namespace ShadowPrototype
{
    [ExecuteAlways]
    public sealed class Mission2SunShape : MonoBehaviour
    {
        private static readonly float[] RayAnglesDegrees = { 90.0f, 30.0f, -30.0f, -90.0f, -150.0f, 150.0f };

        [SerializeField] private Color sunColor = new Color(0.105f, 0.07f, 0.045f, 1.0f);
        [SerializeField, Min(0.05f)] private float centerRadius = 0.68f;
        [SerializeField, Min(0.05f)] private float rayInnerRadius = 1.08f;
        [SerializeField, Min(0.05f)] private float rayOuterRadius = 1.72f;
        [SerializeField, Min(0.05f)] private float rayBaseWidth = 0.58f;
        [SerializeField, Range(24, 160)] private int circleSegments = 72;
        [SerializeField] private int sortingOrder = 25;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh runtimeMesh;
        private Material runtimeMaterial;

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            Rebuild();
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(runtimeMesh);
            DestroyRuntimeObject(runtimeMaterial);
        }

        private void Rebuild()
        {
            EnsureComponents();
            EnsureMaterial();

            Mesh nextMesh = BuildSunMesh();
            Mesh previousMesh = runtimeMesh;
            runtimeMesh = nextMesh;
            meshFilter.sharedMesh = runtimeMesh;

            if (previousMesh != null && previousMesh != runtimeMesh)
            {
                DestroyRuntimeObject(previousMesh);
            }

            meshRenderer.sharedMaterial = runtimeMaterial;
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    meshFilter = gameObject.AddComponent<MeshFilter>();
                }
            }

            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                {
                    meshRenderer = gameObject.AddComponent<MeshRenderer>();
                }
            }
        }

        private void EnsureMaterial()
        {
            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }

                if (shader == null)
                {
                    return;
                }

                runtimeMaterial = new Material(shader)
                {
                    name = "Mission2Sun_Runtime",
                    hideFlags = HideFlags.DontSave
                };
            }

            SetMaterialColor(runtimeMaterial, sunColor);
            if (runtimeMaterial.HasProperty("_Cull"))
            {
                runtimeMaterial.SetFloat("_Cull", 0.0f);
            }
        }

        private Mesh BuildSunMesh()
        {
            int segmentCount = Mathf.Max(24, circleSegments);
            List<Vector3> vertices = new List<Vector3>(segmentCount + 1 + (RayAnglesDegrees.Length * 3));
            List<int> triangles = new List<int>((segmentCount + RayAnglesDegrees.Length) * 3);

            AddCircle(vertices, triangles, segmentCount);
            for (int i = 0; i < RayAnglesDegrees.Length; i++)
            {
                AddRay(vertices, triangles, RayAnglesDegrees[i]);
            }

            Mesh mesh = new Mesh
            {
                name = "Mission2SunShape_Runtime",
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private void AddCircle(List<Vector3> vertices, List<int> triangles, int segmentCount)
        {
            int centerIndex = vertices.Count;
            vertices.Add(Vector3.zero);

            for (int i = 0; i < segmentCount; i++)
            {
                float angle = (Mathf.PI * 2.0f * i) / segmentCount;
                vertices.Add(new Vector3(Mathf.Cos(angle) * centerRadius, Mathf.Sin(angle) * centerRadius, 0.0f));
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int current = centerIndex + 1 + i;
                int next = centerIndex + 1 + ((i + 1) % segmentCount);
                triangles.Add(centerIndex);
                triangles.Add(next);
                triangles.Add(current);
            }
        }

        private void AddRay(List<Vector3> vertices, List<int> triangles, float angleDegrees)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 tangent = new Vector2(-direction.y, direction.x);
            Vector2 baseCenter = direction * rayInnerRadius;
            Vector2 tip = direction * rayOuterRadius;
            Vector2 left = baseCenter + (tangent * rayBaseWidth * 0.5f);
            Vector2 right = baseCenter - (tangent * rayBaseWidth * 0.5f);

            int startIndex = vertices.Count;
            vertices.Add(new Vector3(left.x, left.y, 0.0f));
            vertices.Add(new Vector3(tip.x, tip.y, 0.0f));
            vertices.Add(new Vector3(right.x, right.y, 0.0f));

            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
        {
            if (runtimeObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeObject);
            }
            else
            {
                DestroyImmediate(runtimeObject);
            }
        }
    }
}
