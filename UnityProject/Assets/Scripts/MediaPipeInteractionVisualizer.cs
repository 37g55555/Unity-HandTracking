using System.Collections.Generic;
using UnityEngine;

namespace ShadowPrototype
{
    public class MediaPipeInteractionVisualizer : MonoBehaviour
    {
        [SerializeField] private MediaPipeMeshDeformationInput deformationInput;
        [SerializeField] private ShadowMeshDeformer targetMeshDeformer;
        [SerializeField] private Camera targetCamera;

        [Header("Hand Shadow")]
        [SerializeField] private bool showHandShadow = true;
        [SerializeField] private Color handShadowColor = new Color(0.42f, 0.42f, 0.42f, 0.48f);
        [SerializeField] private float screenHandShadowDistance = 2.0f;
        [SerializeField] private float screenHandShadowScale = 1.0f;
        [SerializeField] private float handShadowFingerWidthScale = 0.055f;
        [SerializeField] private Color handShadowOutlineColor = Color.white;
        [SerializeField] private float handShadowOutlineScale = 1.08f;

        private static readonly Color HoverColor = new Color(0.45f, 0.78f, 1.0f);
        private static readonly Color PullColor = new Color(1.0f, 0.86f, 0.28f);

        private const float MinimumMarkerSize = 0.025f;
        private const float FixedMarkerSize = 0.15f;
        private const int JointCapSegments = 14;
        private const float HandShadowSmoothingSpeed = 18.0f;
        private const float MinimumHandShadowWidthLocal = 0.025f;

        private static readonly int[][] FingerChains =
        {
            new[] { 2, 3, 4 },
            new[] { 5, 6, 7, 8 },
            new[] { 9, 10, 11, 12 },
            new[] { 13, 14, 15, 16 },
            new[] { 17, 18, 19, 20 }
        };

        private static readonly int[] PalmLandmarkIndices = { 0, 1, 2, 5, 9, 13, 17 };

        private readonly Transform[] boundaryMarkers = new Transform[MediaPipeMeshDeformationInput.MaxHands];

        private GameObject handShadowObject;
        private Mesh handShadowMesh;
        private Mesh handShadowOutlineMesh;
        private MeshFilter handShadowMeshFilter;
        private MeshFilter handShadowOutlineMeshFilter;
        private MeshRenderer handShadowMeshRenderer;
        private MeshRenderer handShadowOutlineMeshRenderer;
        private Material handShadowMaterial;
        private Material handShadowOutlineMaterial;
        private readonly Vector2[][] rawHandShadowPoints = CreateHandPointBuffer();
        private readonly Vector2[][] smoothedHandShadowPoints = CreateHandPointBuffer();
        private readonly bool[] activeHandShadows = new bool[MediaPipeMeshDeformationInput.MaxHands];
        private readonly bool[] hasSmoothedHandShadow = new bool[MediaPipeMeshDeformationInput.MaxHands];
        private readonly List<Vector3> handShadowVertices = new List<Vector3>(256);
        private readonly List<int> handShadowTriangles = new List<int>(512);
        private float handShadowVertexZ;

        private void Awake()
        {
            EnsureVisualObjects();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            EnsureVisualObjects();

            if (deformationInput == null || targetMeshDeformer == null || !targetMeshDeformer.HasMesh)
            {
                SetVisible(false);
                return;
            }

            UpdateHandShadow();
            SetInteractionVisible(false);

            float markerSize = ComputeMarkerSize();
            for (int handIndex = 0; handIndex < MediaPipeMeshDeformationInput.MaxHands; handIndex++)
            {
                if (!deformationInput.TryGetHandInteractionState(
                        handIndex,
                        out MediaPipeMeshDeformationInput.HandInteractionSnapshot handState))
                {
                    continue;
                }

                UpdateHandInteractionVisual(handIndex, handState, markerSize);
            }

        }

        private void EnsureVisualObjects()
        {
            for (int i = 0; i < MediaPipeMeshDeformationInput.MaxHands; i++)
            {
                if (boundaryMarkers[i] == null)
                {
                    boundaryMarkers[i] = CreateMarker($"Hand {i + 1} Boundary Marker", HoverColor).transform;
                }

            }

            if (showHandShadow)
            {
                EnsureHandShadowObject();
            }

        }

        private float ComputeMarkerSize()
        {
            return Mathf.Max(MinimumMarkerSize, FixedMarkerSize);
        }

        private void UpdateHandInteractionVisual(
            int handIndex,
            MediaPipeMeshDeformationInput.HandInteractionSnapshot handState,
            float markerSize)
        {
            if (handState.HasActiveBoundaryTarget)
            {
                Color boundaryColor = handState.IsGrabLocked ? PullColor : HoverColor;

                UpdateMarker(boundaryMarkers[handIndex], handState.ActiveBoundaryWorldPoint, markerSize, boundaryColor);
            }
        }

        private void UpdateHandShadow()
        {
            if (!showHandShadow || deformationInput == null || targetMeshDeformer == null)
            {
                SetHandShadowVisible(false);
                return;
            }

            EnsureHandShadowObject();
            MediaPipeUdpReceiver receiver = deformationInput.Receiver;
            if (receiver == null || !receiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                SetHandShadowVisible(false);
                return;
            }

            int handCount = Mathf.Min(
                MediaPipeMeshDeformationInput.MaxHands,
                landmarks.Length / MediaPipeMeshDeformationInput.LandmarksPerHand);
            bool hasAnyHandShadow = false;

            for (int handIndex = 0; handIndex < activeHandShadows.Length; handIndex++)
            {
                activeHandShadows[handIndex] = false;

                if (handIndex >= handCount ||
                    !TryUpdateHandShadowPoints(handIndex, landmarks, rawHandShadowPoints[handIndex]))
                {
                    hasSmoothedHandShadow[handIndex] = false;
                    continue;
                }

                SmoothHandShadowPoints(handIndex);
                activeHandShadows[handIndex] = true;
                hasAnyHandShadow = true;
            }

            if (!hasAnyHandShadow)
            {
                SetHandShadowVisible(false);
                return;
            }

            BuildHandShadowMesh();
            SetHandShadowVisible(true, false);
        }

        private bool TryUpdateHandShadowPoints(int handIndex, Vector3[] landmarks, Vector2[] points)
        {
            int startIndex = handIndex * MediaPipeMeshDeformationInput.LandmarksPerHand;
            for (int landmarkIndex = 0; landmarkIndex < MediaPipeMeshDeformationInput.LandmarksPerHand; landmarkIndex++)
            {
                int absoluteIndex = startIndex + landmarkIndex;
                if (absoluteIndex < 0 || absoluteIndex >= landmarks.Length)
                {
                    return false;
                }

                Vector2 trackedPoint = new Vector2(landmarks[absoluteIndex].x, landmarks[absoluteIndex].y);
                if (!TryProjectHandShadowPoint(trackedPoint, out Vector2 localPoint))
                {
                    return false;
                }

                points[landmarkIndex] = localPoint;
            }

            return true;
        }

        private bool TryProjectHandShadowPoint(Vector2 trackedPoint, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (targetCamera == null)
            {
                return false;
            }

            Vector3 viewportPoint = new Vector3(
                Mathf.Clamp01(trackedPoint.x / MediaPipeMeshDeformationInput.TrackedFrameWidth),
                Mathf.Clamp01(trackedPoint.y / MediaPipeMeshDeformationInput.TrackedFrameHeight),
                screenHandShadowDistance);
            Vector3 worldPoint = targetCamera.ViewportToWorldPoint(viewportPoint);
            Vector3 cameraLocalPoint = targetCamera.transform.InverseTransformPoint(worldPoint);
            handShadowVertexZ = cameraLocalPoint.z;
            localPoint = new Vector2(cameraLocalPoint.x, cameraLocalPoint.y) * screenHandShadowScale;
            return true;
        }

        private void SmoothHandShadowPoints(int handIndex)
        {
            if (!hasSmoothedHandShadow[handIndex])
            {
                CopyHandShadowPoints(rawHandShadowPoints[handIndex], smoothedHandShadowPoints[handIndex]);
                hasSmoothedHandShadow[handIndex] = true;
                return;
            }

            float blend = 1.0f - Mathf.Exp(-HandShadowSmoothingSpeed * Time.deltaTime);
            for (int i = 0; i < MediaPipeMeshDeformationInput.LandmarksPerHand; i++)
            {
                smoothedHandShadowPoints[handIndex][i] = Vector2.Lerp(
                    smoothedHandShadowPoints[handIndex][i],
                    rawHandShadowPoints[handIndex][i],
                    blend);
            }
        }

        private static void CopyHandShadowPoints(Vector2[] source, Vector2[] destination)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        private void BuildHandShadowMesh()
        {
            if (handShadowMesh == null)
            {
                return;
            }

            handShadowVertices.Clear();
            handShadowTriangles.Clear();
            List<Vector3> outlineVertices = new List<Vector3>(256);

            for (int handIndex = 0; handIndex < activeHandShadows.Length; handIndex++)
            {
                if (activeHandShadows[handIndex])
                {
                    int firstHandVertexIndex = handShadowVertices.Count;
                    AddHandShadowGeometry(smoothedHandShadowPoints[handIndex]);
                    AddHandOutlineVertices(firstHandVertexIndex, outlineVertices);
                }
            }

            handShadowMesh.Clear();
            handShadowMesh.SetVertices(handShadowVertices);
            handShadowMesh.SetTriangles(handShadowTriangles, 0);
            handShadowMesh.RecalculateBounds();

            UpdateHandShadowOutlineMesh(outlineVertices);
        }

        private void AddHandOutlineVertices(int firstHandVertexIndex, List<Vector3> outlineVertices)
        {
            int handVertexCount = handShadowVertices.Count - firstHandVertexIndex;
            if (handVertexCount <= 0)
            {
                return;
            }

            Vector3 center = Vector3.zero;
            for (int i = firstHandVertexIndex; i < handShadowVertices.Count; i++)
            {
                center += handShadowVertices[i];
            }

            center /= handVertexCount;
            for (int i = firstHandVertexIndex; i < handShadowVertices.Count; i++)
            {
                Vector3 vertex = handShadowVertices[i];
                Vector3 offset = vertex - center;
                outlineVertices.Add(center + (offset * handShadowOutlineScale));
            }
        }

        private void UpdateHandShadowOutlineMesh(List<Vector3> outlineVertices)
        {
            if (handShadowOutlineMesh == null)
            {
                return;
            }

            handShadowOutlineMesh.Clear();
            if (outlineVertices.Count == 0)
            {
                return;
            }

            handShadowOutlineMesh.SetVertices(outlineVertices);
            handShadowOutlineMesh.SetTriangles(handShadowTriangles, 0);
            handShadowOutlineMesh.RecalculateBounds();
        }

        private void AddHandShadowGeometry(Vector2[] points)
        {
            float capWidth = ComputeHandShadowCapWidth(points);
            float fingerWidth = ComputeHandShadowFingerWidth(points);
            AddPalmShadow(points, capWidth * 1.15f);

            for (int i = 0; i < FingerChains.Length; i++)
            {
                int[] chain = FingerChains[i];
                for (int j = 0; j < chain.Length - 1; j++)
                {
                    AddSegmentShadow(points[chain[j]], points[chain[j + 1]], fingerWidth);
                }
            }

            for (int i = 0; i < points.Length; i++)
            {
                if (i == 0 || i == 1)
                {
                    continue;
                }

                float radius = capWidth * 0.52f;
                if (IsFingerTip(i))
                {
                    radius = capWidth * 0.68f;
                }

                AddDiscShadow(points[i], radius, JointCapSegments);
            }
        }

        private float ComputeHandShadowCapWidth(Vector2[] points)
        {
            Vector2 min = points[0];
            Vector2 max = points[0];
            for (int i = 1; i < points.Length; i++)
            {
                min = new Vector2(Mathf.Min(min.x, points[i].x), Mathf.Min(min.y, points[i].y));
                max = new Vector2(Mathf.Max(max.x, points[i].x), Mathf.Max(max.y, points[i].y));
            }

            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            return Mathf.Max(MinimumHandShadowWidthLocal, span * 0.055f);
        }

        private float ComputeHandShadowFingerWidth(Vector2[] points)
        {
            Vector2 min = points[0];
            Vector2 max = points[0];
            for (int i = 1; i < points.Length; i++)
            {
                min = new Vector2(Mathf.Min(min.x, points[i].x), Mathf.Min(min.y, points[i].y));
                max = new Vector2(Mathf.Max(max.x, points[i].x), Mathf.Max(max.y, points[i].y));
            }

            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            return Mathf.Max(MinimumHandShadowWidthLocal, span * handShadowFingerWidthScale);
        }

        private void AddPalmShadow(Vector2[] points, float padding)
        {
            List<Vector2> palmPoints = new List<Vector2>(PalmLandmarkIndices.Length);
            for (int i = 0; i < PalmLandmarkIndices.Length; i++)
            {
                palmPoints.Add(points[PalmLandmarkIndices[i]]);
            }

            List<Vector2> hull = BuildConvexHull(palmPoints);
            if (hull.Count < 3)
            {
                return;
            }

            Vector2 center = Vector2.zero;
            for (int i = 0; i < hull.Count; i++)
            {
                center += hull[i];
            }

            center /= hull.Count;
            int centerIndex = AddHandShadowVertex(center);
            int firstOuterIndex = handShadowVertices.Count;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 direction = hull[i] - center;
                if (direction.sqrMagnitude > 0.000001f)
                {
                    direction.Normalize();
                }

                AddHandShadowVertex(hull[i] + (direction * padding));
            }

            for (int i = 0; i < hull.Count; i++)
            {
                int current = firstOuterIndex + i;
                int next = firstOuterIndex + ((i + 1) % hull.Count);
                handShadowTriangles.Add(centerIndex);
                handShadowTriangles.Add(current);
                handShadowTriangles.Add(next);
            }
        }

        private void AddSegmentShadow(Vector2 start, Vector2 end, float width)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            direction.Normalize();
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            int baseIndex = handShadowVertices.Count;
            AddHandShadowVertex(start + normal);
            AddHandShadowVertex(end + normal);
            AddHandShadowVertex(end - normal);
            AddHandShadowVertex(start - normal);

            handShadowTriangles.Add(baseIndex);
            handShadowTriangles.Add(baseIndex + 1);
            handShadowTriangles.Add(baseIndex + 2);
            handShadowTriangles.Add(baseIndex);
            handShadowTriangles.Add(baseIndex + 2);
            handShadowTriangles.Add(baseIndex + 3);
        }

        private void AddDiscShadow(Vector2 center, float radius, int segmentCount)
        {
            int centerIndex = AddHandShadowVertex(center);
            int firstOuterIndex = handShadowVertices.Count;

            for (int i = 0; i < segmentCount; i++)
            {
                float t = (float)i / segmentCount;
                float angle = t * Mathf.PI * 2.0f;
                AddHandShadowVertex(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int current = firstOuterIndex + i;
                int next = firstOuterIndex + ((i + 1) % segmentCount);
                handShadowTriangles.Add(centerIndex);
                handShadowTriangles.Add(current);
                handShadowTriangles.Add(next);
            }
        }

        private int AddHandShadowVertex(Vector2 localPoint)
        {
            handShadowVertices.Add(new Vector3(localPoint.x, localPoint.y, handShadowVertexZ));
            return handShadowVertices.Count - 1;
        }

        private void UpdateMarker(Transform marker, Vector3 position, float markerSize, Color color)
        {
            marker.gameObject.SetActive(true);
            marker.position = position;
            marker.localScale = Vector3.one * markerSize;

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                {
                    renderer.sharedMaterial.SetColor("_BaseColor", color);
                }

                if (renderer.sharedMaterial.HasProperty("_Color"))
                {
                    renderer.sharedMaterial.SetColor("_Color", color);
                }
            }
        }

        private void SetVisible(bool visible)
        {
            SetInteractionVisible(visible);
            if (!visible)
            {
                SetHandShadowVisible(false);
            }
        }

        private void SetInteractionVisible(bool visible)
        {
            for (int i = 0; i < MediaPipeMeshDeformationInput.MaxHands; i++)
            {
                if (boundaryMarkers[i] != null) boundaryMarkers[i].gameObject.SetActive(false);
            }
        }

        private void EnsureHandShadowObject()
        {
            if (targetMeshDeformer == null)
            {
                return;
            }

            if (handShadowObject == null)
            {
                handShadowObject = new GameObject("Hand Shadow Silhouette");
                handShadowMeshFilter = handShadowObject.AddComponent<MeshFilter>();
                handShadowMeshRenderer = handShadowObject.AddComponent<MeshRenderer>();
                handShadowMesh = new Mesh
                {
                    name = "MediaPipeHandShadow_Runtime"
                };
                handShadowOutlineMesh = new Mesh
                {
                    name = "MediaPipeHandShadowOutline_Runtime"
                };
                handShadowMesh.MarkDynamic();
                handShadowOutlineMesh.MarkDynamic();
                handShadowMeshFilter.sharedMesh = handShadowMesh;
                handShadowMaterial = CreateTransparentUnlitMaterial(handShadowColor);
                handShadowMeshRenderer.sharedMaterial = handShadowMaterial;
                handShadowMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                handShadowMeshRenderer.receiveShadows = false;
                handShadowMeshRenderer.sortingOrder = 500;

                GameObject outlineObject = new GameObject("Hand Shadow Outline");
                outlineObject.transform.SetParent(handShadowObject.transform, false);
                handShadowOutlineMeshFilter = outlineObject.AddComponent<MeshFilter>();
                handShadowOutlineMeshRenderer = outlineObject.AddComponent<MeshRenderer>();
                handShadowOutlineMeshFilter.sharedMesh = handShadowOutlineMesh;
                handShadowOutlineMaterial = CreateTransparentUnlitMaterial(handShadowOutlineColor);
                handShadowOutlineMeshRenderer.sharedMaterial = handShadowOutlineMaterial;
                handShadowOutlineMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                handShadowOutlineMeshRenderer.receiveShadows = false;
                handShadowOutlineMeshRenderer.sortingOrder = 499;
                handShadowObject.SetActive(false);
            }

            if (handShadowMeshFilter == null)
            {
                handShadowMeshFilter = handShadowObject.GetComponent<MeshFilter>();
            }

            if (handShadowMeshRenderer == null)
            {
                handShadowMeshRenderer = handShadowObject.GetComponent<MeshRenderer>();
            }

            if (handShadowOutlineMeshFilter == null)
            {
                Transform outlineTransform = handShadowObject.transform.Find("Hand Shadow Outline");
                handShadowOutlineMeshFilter = outlineTransform != null
                    ? outlineTransform.GetComponent<MeshFilter>()
                    : null;
            }

            if (handShadowOutlineMeshRenderer == null)
            {
                Transform outlineTransform = handShadowObject.transform.Find("Hand Shadow Outline");
                handShadowOutlineMeshRenderer = outlineTransform != null
                    ? outlineTransform.GetComponent<MeshRenderer>()
                    : null;
            }

            Transform desiredParent = targetCamera != null
                ? targetCamera.transform
                : targetMeshDeformer.transform;

            if (handShadowObject.transform.parent != desiredParent)
            {
                handShadowObject.transform.SetParent(desiredParent, false);
                handShadowObject.transform.localPosition = Vector3.zero;
                handShadowObject.transform.localRotation = Quaternion.identity;
                handShadowObject.transform.localScale = Vector3.one;
            }

            if (handShadowMaterial != null)
            {
                ConfigureTransparentMaterial(handShadowMaterial, handShadowColor);
            }

            if (handShadowOutlineMaterial != null)
            {
                ConfigureTransparentMaterial(handShadowOutlineMaterial, handShadowOutlineColor);
            }
        }

        private void SetHandShadowVisible(bool visible, bool resetSmoothing = true)
        {
            if (handShadowObject != null)
            {
                handShadowObject.SetActive(visible);
            }

            if (!visible && resetSmoothing)
            {
                for (int i = 0; i < hasSmoothedHandShadow.Length; i++)
                {
                    hasSmoothedHandShadow[i] = false;
                    activeHandShadows[i] = false;
                }
            }
        }

        private GameObject CreateMarker(string name, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(transform, false);

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Material material = CreateUnlitMaterial(color);
            Renderer renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return marker;
        }

        private static Vector2[][] CreateHandPointBuffer()
        {
            Vector2[][] buffer = new Vector2[MediaPipeMeshDeformationInput.MaxHands][];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = new Vector2[MediaPipeMeshDeformationInput.LandmarksPerHand];
            }

            return buffer;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static Material CreateTransparentUnlitMaterial(Color color)
        {
            Material material = CreateUnlitMaterial(color);
            ConfigureTransparentMaterial(material, color);
            return material;
        }

        private static void ConfigureTransparentMaterial(Material material, Color color)
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

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1.0f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0.0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0.0f);
            }

            if (material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3020;
        }

        private static bool IsFingerTip(int landmarkIndex)
        {
            return landmarkIndex == 4 ||
                   landmarkIndex == 8 ||
                   landmarkIndex == 12 ||
                   landmarkIndex == 16 ||
                   landmarkIndex == 20;
        }

        private static List<Vector2> BuildConvexHull(List<Vector2> points)
        {
            List<Vector2> sorted = new List<Vector2>(points);
            sorted.Sort(CompareVector2);

            List<Vector2> lower = new List<Vector2>();
            for (int i = 0; i < sorted.Count; i++)
            {
                while (lower.Count >= 2 &&
                       Cross(lower[lower.Count - 1] - lower[lower.Count - 2], sorted[i] - lower[lower.Count - 1]) <= 0.0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }

                lower.Add(sorted[i]);
            }

            List<Vector2> upper = new List<Vector2>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                while (upper.Count >= 2 &&
                       Cross(upper[upper.Count - 1] - upper[upper.Count - 2], sorted[i] - upper[upper.Count - 1]) <= 0.0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }

                upper.Add(sorted[i]);
            }

            if (lower.Count > 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            if (upper.Count > 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            lower.AddRange(upper);
            return lower;
        }

        private static int CompareVector2(Vector2 a, Vector2 b)
        {
            int compareX = a.x.CompareTo(b.x);
            if (compareX != 0)
            {
                return compareX;
            }

            return a.y.CompareTo(b.y);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.x * b.y) - (a.y * b.x);
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(handShadowMesh);
            DestroyRuntimeObject(handShadowOutlineMesh);
            DestroyRuntimeObject(handShadowMaterial);
            DestroyRuntimeObject(handShadowOutlineMaterial);
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
