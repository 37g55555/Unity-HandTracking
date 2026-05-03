using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowPrototype
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class ShadowDeformer : MonoBehaviour
    {
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        [SerializeField] private MeshCollider meshCollider;
        [SerializeField] private float transientRelaxationSpeed = 6.0f;
        [SerializeField] private float colliderRefreshInterval = 0.08f;

        private Mesh runtimeMesh;
        private int[] boundaryIndices = new int[0];
        private Vector3[] baseVertices = Array.Empty<Vector3>();
        private Vector3[] workingVertices = Array.Empty<Vector3>();
        private Vector3[] persistentOffsets = Array.Empty<Vector3>();
        private Vector3[] transientOffsets = Array.Empty<Vector3>();
        private int[] baseTriangles = Array.Empty<int>();
        private bool[] removedTriangleMask = Array.Empty<bool>();
        private int[] activeTriangles = Array.Empty<int>();
        private bool topologyDirty;
        private bool meshDirty;
        private float colliderRefreshTimer;

        public Mesh CurrentMesh => runtimeMesh;
        public int[] BoundaryIndices => boundaryIndices;
        public bool HasMesh => runtimeMesh != null && workingVertices.Length > 0;

        public Bounds GetWorldBounds()
        {
            EnsureComponents();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return new Bounds(transform.position, Vector3.zero);
            }

            return TransformBounds(meshFilter.transform.localToWorldMatrix, meshFilter.sharedMesh.bounds);
        }

        public void Configure(MeshFilter filter, MeshRenderer renderer, MeshCollider collider)
        {
            meshFilter = filter;
            meshRenderer = renderer;
            meshCollider = collider;
            EnsureComponents();
        }

        public void ReplaceMesh(Mesh newMesh, int[] newBoundaryIndices = null)
        {
            if (newMesh == null)
            {
                Debug.LogWarning("ShadowDeformer received a null mesh and kept the current mesh.");
                return;
            }

            EnsureComponents();

            newMesh.name = $"{newMesh.name}_Runtime";
            newMesh.RecalculateNormals();
            newMesh.RecalculateBounds();

            if (runtimeMesh != null)
            {
                DestroyMesh(runtimeMesh);
            }

            runtimeMesh = newMesh;
            boundaryIndices = newBoundaryIndices ?? new int[0];
            CacheMeshData(runtimeMesh);

            meshFilter.sharedMesh = runtimeMesh;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = runtimeMesh;
        }

        public void ResetToBaseShape()
        {
            if (!HasMesh)
            {
                return;
            }

            Array.Clear(persistentOffsets, 0, persistentOffsets.Length);
            Array.Clear(transientOffsets, 0, transientOffsets.Length);
            Array.Clear(removedTriangleMask, 0, removedTriangleMask.Length);
            topologyDirty = true;
            meshDirty = true;
            CommitMesh(forceColliderRefresh: true);
        }

        public bool ContainsLocalPoint(Vector2 localPoint)
        {
            if (!HasMesh)
            {
                return false;
            }

            int[] triangles = GetActiveTriangles();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 a = GetCurrentVertex2D(triangles[i]);
                Vector2 b = GetCurrentVertex2D(triangles[i + 1]);
                Vector2 c = GetCurrentVertex2D(triangles[i + 2]);

                if (IsPointInsideTriangle(localPoint, a, b, c))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetNearestBoundaryVertex(
            Vector2 localPoint,
            out int boundaryArrayIndex,
            out int meshVertexIndex,
            out Vector2 boundaryLocalPoint,
            out Vector3 boundaryWorldPoint)
        {
            boundaryArrayIndex = -1;
            meshVertexIndex = -1;
            boundaryLocalPoint = Vector2.zero;
            boundaryWorldPoint = Vector3.zero;

            if (!HasMesh || boundaryIndices == null || boundaryIndices.Length == 0)
            {
                return false;
            }

            float bestDistanceSquared = float.PositiveInfinity;
            for (int i = 0; i < boundaryIndices.Length; i++)
            {
                int candidateVertexIndex = boundaryIndices[i];
                if (candidateVertexIndex < 0 || candidateVertexIndex >= workingVertices.Length)
                {
                    continue;
                }

                Vector2 candidate = GetCurrentVertex2D(candidateVertexIndex);
                float distanceSquared = (candidate - localPoint).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                boundaryArrayIndex = i;
                meshVertexIndex = candidateVertexIndex;
                boundaryLocalPoint = candidate;
            }

            if (meshVertexIndex < 0)
            {
                return false;
            }

            boundaryWorldPoint = transform.TransformPoint(new Vector3(boundaryLocalPoint.x, boundaryLocalPoint.y, 0.0f));
            return true;
        }

        public bool TryGetBoundaryVertexAtBoundaryIndex(
            int boundaryArrayIndex,
            out int meshVertexIndex,
            out Vector2 boundaryLocalPoint,
            out Vector3 boundaryWorldPoint)
        {
            meshVertexIndex = -1;
            boundaryLocalPoint = Vector2.zero;
            boundaryWorldPoint = Vector3.zero;

            if (!HasMesh || boundaryIndices == null || boundaryArrayIndex < 0 || boundaryArrayIndex >= boundaryIndices.Length)
            {
                return false;
            }

            meshVertexIndex = boundaryIndices[boundaryArrayIndex];
            if (meshVertexIndex < 0 || meshVertexIndex >= workingVertices.Length)
            {
                meshVertexIndex = -1;
                return false;
            }

            boundaryLocalPoint = GetCurrentVertex2D(meshVertexIndex);
            boundaryWorldPoint = transform.TransformPoint(new Vector3(boundaryLocalPoint.x, boundaryLocalPoint.y, 0.0f));
            return true;
        }

        public bool ApplyPush(Vector2 localPoint, float radius, float strength)
        {
            if (!HasMesh || radius <= 0.0f || strength <= 0.0f)
            {
                return false;
            }

            float radiusSquared = radius * radius;
            bool affected = false;

            for (int i = 0; i < workingVertices.Length; i++)
            {
                Vector2 vertex = GetCurrentVertex2D(i);
                Vector2 toPoint = localPoint - vertex;
                float distanceSquared = toPoint.sqrMagnitude;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                float falloff = 1.0f - Mathf.Clamp01(distance / radius);
                Vector2 direction = distance > 0.0001f ? toPoint / distance : UnityEngine.Random.insideUnitCircle.normalized;
                Vector3 delta = new Vector3(direction.x, direction.y, 0.0f) * (strength * falloff * falloff);
                transientOffsets[i] += delta;
                affected = true;
            }

            if (!affected)
            {
                return false;
            }

            meshDirty = true;
            return true;
        }

        public bool ApplyPull(Vector2 localPoint, Vector2 delta, float radius, float strength)
        {
            if (!HasMesh || radius <= 0.0f || strength <= 0.0f || delta.sqrMagnitude <= 0.0f)
            {
                return false;
            }

            float radiusSquared = radius * radius;
            bool affected = false;

            for (int i = 0; i < workingVertices.Length; i++)
            {
                Vector2 vertex = GetCurrentVertex2D(i);
                float distanceSquared = (vertex - localPoint).sqrMagnitude;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                float falloff = 1.0f - Mathf.Clamp01(distance / radius);
                Vector3 offset = new Vector3(delta.x, delta.y, 0.0f) * (strength * falloff * falloff);
                persistentOffsets[i] += offset;
                affected = true;
            }

            if (!affected)
            {
                return false;
            }

            meshDirty = true;
            return true;
        }

        public bool ApplyTear(Vector2 startLocalPoint, Vector2 endLocalPoint, float width, float separation)
        {
            if (!HasMesh || width <= 0.0f || separation <= 0.0f)
            {
                return false;
            }

            Vector2 segment = endLocalPoint - startLocalPoint;
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.001f)
            {
                return false;
            }

            Vector2 segmentDirection = segment / segmentLength;
            Vector2 separationNormal = new Vector2(-segmentDirection.y, segmentDirection.x);
            bool affected = false;

            for (int triangleIndex = 0; triangleIndex < removedTriangleMask.Length; triangleIndex++)
            {
                if (removedTriangleMask[triangleIndex])
                {
                    continue;
                }

                int first = baseTriangles[triangleIndex * 3];
                int second = baseTriangles[(triangleIndex * 3) + 1];
                int third = baseTriangles[(triangleIndex * 3) + 2];
                Vector2 centroid = (GetCurrentVertex2D(first) + GetCurrentVertex2D(second) + GetCurrentVertex2D(third)) / 3.0f;
                float distanceToSegment = DistanceToSegment(centroid, startLocalPoint, endLocalPoint);
                if (distanceToSegment <= width * 0.55f)
                {
                    removedTriangleMask[triangleIndex] = true;
                    topologyDirty = true;
                    affected = true;
                }
            }

            for (int i = 0; i < workingVertices.Length; i++)
            {
                Vector2 vertex = GetCurrentVertex2D(i);
                float distanceToSegment = DistanceToSegment(vertex, startLocalPoint, endLocalPoint);
                if (distanceToSegment > width)
                {
                    continue;
                }

                float signedSide = SignedDistanceToLine(vertex, startLocalPoint, segmentDirection);
                float direction = signedSide >= 0.0f ? 1.0f : -1.0f;
                float falloff = 1.0f - Mathf.Clamp01(distanceToSegment / width);
                Vector3 offset = new Vector3(separationNormal.x, separationNormal.y, 0.0f) * (direction * separation * falloff * falloff);
                persistentOffsets[i] += offset;
                affected = true;
            }

            if (!affected)
            {
                return false;
            }

            meshDirty = true;
            return true;
        }

        private void Awake()
        {
            EnsureComponents();
        }

        private void Reset()
        {
            EnsureComponents();
        }

        private void OnDestroy()
        {
            if (runtimeMesh != null)
            {
                DestroyMesh(runtimeMesh);
                runtimeMesh = null;
            }
        }

        private void LateUpdate()
        {
            if (!HasMesh)
            {
                return;
            }

            bool transientChanged = RelaxTransientOffsets();
            if (!meshDirty && !transientChanged)
            {
                return;
            }

            CommitMesh();
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
            }

            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
            }
        }

        private void CacheMeshData(Mesh mesh)
        {
            baseVertices = mesh.vertices;
            workingVertices = (Vector3[])baseVertices.Clone();
            persistentOffsets = new Vector3[baseVertices.Length];
            transientOffsets = new Vector3[baseVertices.Length];
            baseTriangles = mesh.triangles;
            removedTriangleMask = new bool[baseTriangles.Length / 3];
            activeTriangles = (int[])baseTriangles.Clone();
            topologyDirty = false;
            meshDirty = false;
            colliderRefreshTimer = 0.0f;
        }

        private bool RelaxTransientOffsets()
        {
            if (transientOffsets.Length == 0)
            {
                return false;
            }

            bool changed = false;
            float blend = 1.0f - Mathf.Exp(-transientRelaxationSpeed * Time.deltaTime);
            for (int i = 0; i < transientOffsets.Length; i++)
            {
                Vector3 next = Vector3.Lerp(transientOffsets[i], Vector3.zero, blend);
                if ((next - transientOffsets[i]).sqrMagnitude <= 0.00000001f)
                {
                    transientOffsets[i] = next;
                    continue;
                }

                transientOffsets[i] = next;
                changed = true;
            }

            return changed;
        }

        private void CommitMesh(bool forceColliderRefresh = false)
        {
            if (!HasMesh)
            {
                return;
            }

            for (int i = 0; i < workingVertices.Length; i++)
            {
                workingVertices[i] = baseVertices[i] + persistentOffsets[i] + transientOffsets[i];
            }

            runtimeMesh.vertices = workingVertices;
            runtimeMesh.triangles = GetActiveTriangles();
            runtimeMesh.RecalculateBounds();
            runtimeMesh.RecalculateNormals();

            colliderRefreshTimer -= Time.deltaTime;
            if (meshCollider != null && (forceColliderRefresh || colliderRefreshTimer <= 0.0f))
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = runtimeMesh;
                colliderRefreshTimer = colliderRefreshInterval;
            }

            meshDirty = false;
        }

        private int[] GetActiveTriangles()
        {
            if (!topologyDirty)
            {
                return activeTriangles;
            }

            List<int> triangles = new List<int>(baseTriangles.Length);
            for (int triangleIndex = 0; triangleIndex < removedTriangleMask.Length; triangleIndex++)
            {
                if (removedTriangleMask[triangleIndex])
                {
                    continue;
                }

                int baseIndex = triangleIndex * 3;
                triangles.Add(baseTriangles[baseIndex]);
                triangles.Add(baseTriangles[baseIndex + 1]);
                triangles.Add(baseTriangles[baseIndex + 2]);
            }

            activeTriangles = triangles.ToArray();
            topologyDirty = false;
            return activeTriangles;
        }

        private Vector2 GetCurrentVertex2D(int vertexIndex)
        {
            Vector3 vertex = baseVertices[vertexIndex] + persistentOffsets[vertexIndex] + transientOffsets[vertexIndex];
            return new Vector2(vertex.x, vertex.y);
        }

        private static bool IsPointInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float denominator = ((b.y - c.y) * (a.x - c.x)) + ((c.x - b.x) * (a.y - c.y));
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                return false;
            }

            float alpha = (((b.y - c.y) * (point.x - c.x)) + ((c.x - b.x) * (point.y - c.y))) / denominator;
            float beta = (((c.y - a.y) * (point.x - c.x)) + ((a.x - c.x) * (point.y - c.y))) / denominator;
            float gamma = 1.0f - alpha - beta;
            return alpha >= 0.0f && beta >= 0.0f && gamma >= 0.0f;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
            {
                return Vector2.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            Vector2 projection = start + (segment * t);
            return Vector2.Distance(point, projection);
        }

        private static float SignedDistanceToLine(Vector2 point, Vector2 linePoint, Vector2 lineDirection)
        {
            Vector2 perpendicular = new Vector2(-lineDirection.y, lineDirection.x);
            return Vector2.Dot(point - linePoint, perpendicular);
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;

            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0.0f, 0.0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0.0f, extents.y, 0.0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, extents.z));

            Vector3 worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
            );

            return new Bounds(center, worldExtents * 2.0f);
        }
    }
}
