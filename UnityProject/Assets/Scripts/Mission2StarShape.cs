using System.Collections.Generic;
using UnityEngine;

namespace ShadowPrototype
{
    [ExecuteAlways]
    public sealed class Mission2StarShape : MonoBehaviour
    {
        private const int StarPointCount = 10;

        [SerializeField] private Color starColor = new Color(0.02f, 0.02f, 0.02f, 1.0f);
        [SerializeField, Min(0.05f)] private float outerRadius = 1.0f;
        [SerializeField, Min(0.01f)] private float innerRadius = 0.43f;
        [SerializeField] private int sortingOrder = 15;

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
            innerRadius = Mathf.Min(innerRadius, outerRadius);
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

            Mesh nextMesh = BuildStarMesh();
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
                    name = "Mission2Star_Runtime",
                    hideFlags = HideFlags.DontSave
                };
            }

            SetMaterialColor(runtimeMaterial, starColor);
            if (runtimeMaterial.HasProperty("_Cull"))
            {
                runtimeMaterial.SetFloat("_Cull", 0.0f);
            }
        }

        private Mesh BuildStarMesh()
        {
            List<Vector3> vertices = new List<Vector3>(StarPointCount + 1);
            List<int> triangles = new List<int>(StarPointCount * 3);

            vertices.Add(Vector3.zero);
            for (int i = 0; i < StarPointCount; i++)
            {
                bool outerPoint = i % 2 == 0;
                float radius = outerPoint ? outerRadius : innerRadius;
                float angle = (Mathf.PI * 0.5f) + (i * Mathf.PI / 5.0f);
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.0f));
            }

            for (int i = 0; i < StarPointCount; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(((i + 1) % StarPointCount) + 1);
            }

            Mesh mesh = new Mesh
            {
                name = "Mission2StarShape_Runtime",
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
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
