using UnityEngine;

namespace ShadowPrototype
{
    public sealed class Mission4DoorTransition : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform shadowStar;
        [SerializeField] private MeshFilter shadowMeshFilter;
        [SerializeField] private Transform door;
        [SerializeField] private SpriteRenderer doorSpriteRenderer;
        [SerializeField] private Mission4Controller mission4Controller;
        [SerializeField] private string fallbackDoorName = "Door";

        [Header("Transition")]
        [SerializeField, Min(0.0f)] private float contactPadding;

        [Header("Silhouette Contact")]
        [SerializeField, Range(1, 255)] private int alphaThreshold = 8;
        [SerializeField, Min(1)] private int doorPixelSampleStep = 1;

        private Renderer[] shadowRenderers;
        private Renderer[] doorRenderers;
        private Texture2D cachedDoorTexture;
        private Color32[] cachedDoorPixels;
        private bool transitionStarted;

        private void Awake()
        {
            if (shadowStar == null)
            {
                shadowStar = transform;
            }

            ResolveReferences();
        }

        private void LateUpdate()
        {
            if (transitionStarted)
            {
                return;
            }

            ResolveReferences();
            if (shadowStar == null || door == null)
            {
                return;
            }

            if (IsTouchingDoor())
            {
                transitionStarted = true;
                ResolveMission4Controller()?.HandleDoorReached();
            }
        }

        private void ResolveReferences()
        {
            if (door == null && !string.IsNullOrWhiteSpace(fallbackDoorName))
            {
                GameObject doorObject = GameObject.Find(fallbackDoorName);
                door = doorObject != null ? doorObject.transform : null;
            }

            if (shadowStar != null && !HasEnabledRenderer(shadowRenderers))
            {
                shadowRenderers = shadowStar.GetComponentsInChildren<Renderer>(true);
            }

            if (shadowMeshFilter == null && shadowStar != null)
            {
                shadowMeshFilter = shadowStar.GetComponentInChildren<MeshFilter>(true);
            }

            if (doorSpriteRenderer == null && door != null)
            {
                doorSpriteRenderer = door.GetComponentInChildren<SpriteRenderer>(true);
            }

            if (door != null && !HasEnabledRenderer(doorRenderers))
            {
                doorRenderers = door.GetComponentsInChildren<Renderer>(true);
            }
        }

        private Mission4Controller ResolveMission4Controller()
        {
            if (mission4Controller == null)
            {
                mission4Controller = FindObjectOfType<Mission4Controller>();
            }

            return mission4Controller;
        }

        private bool IsTouchingDoor()
        {
            if (TryGetBounds(shadowRenderers, out Bounds shadowBounds) &&
                TryGetBounds(doorRenderers, out Bounds doorBounds))
            {
                shadowBounds.Expand(contactPadding * 2.0f);
                doorBounds.Expand(contactPadding * 2.0f);
                if (!OverlapsXY(shadowBounds, doorBounds))
                {
                    return false;
                }

                return IsShadowTouchingDoorSilhouette(shadowBounds);
            }

            return false;
        }

        private bool IsShadowTouchingDoorSilhouette(Bounds shadowBounds)
        {
            if (shadowMeshFilter == null ||
                doorSpriteRenderer == null ||
                doorSpriteRenderer.sprite == null)
            {
                return false;
            }

            Mesh shadowMesh = shadowMeshFilter.sharedMesh;
            if (shadowMesh == null)
            {
                return false;
            }

            Texture2D doorTexture = doorSpriteRenderer.sprite.texture;
            if (doorTexture == null)
            {
                return false;
            }

            if (!TryGetDoorPixelSearchRect(shadowBounds, out RectInt pixelRect))
            {
                return false;
            }

            Sprite doorSprite = doorSpriteRenderer.sprite;
            Rect textureRect = doorSprite.textureRect;
            Vector2 pivot = doorSprite.pivot;
            float pixelsPerUnit = Mathf.Max(1.0f, doorSprite.pixelsPerUnit);
            int step = Mathf.Max(1, doorPixelSampleStep);
            if (!TryGetDoorPixels(doorTexture, out Color32[] pixels))
            {
                return false;
            }

            Vector3 doorLocalPoint = Vector3.zero;
            int textureWidth = doorTexture.width;
            int threshold = Mathf.Clamp(alphaThreshold, 1, 255);

            for (int y = pixelRect.yMin; y < pixelRect.yMax; y += step)
            {
                for (int x = pixelRect.xMin; x < pixelRect.xMax; x += step)
                {
                    int pixelIndex = (y * textureWidth) + x;
                    if (pixelIndex < 0 || pixelIndex >= pixels.Length || pixels[pixelIndex].a < threshold)
                    {
                        continue;
                    }

                    doorLocalPoint.x = ((x + 0.5f) - textureRect.x - pivot.x) / pixelsPerUnit;
                    doorLocalPoint.y = ((y + 0.5f) - textureRect.y - pivot.y) / pixelsPerUnit;
                    Vector3 worldPoint = doorSpriteRenderer.transform.TransformPoint(doorLocalPoint);
                    Vector3 shadowLocalPoint = shadowMeshFilter.transform.InverseTransformPoint(worldPoint);

                    if (IsPointInsideMeshXY(shadowLocalPoint, shadowMesh))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetDoorPixelSearchRect(Bounds shadowBounds, out RectInt pixelRect)
        {
            pixelRect = default;
            if (doorSpriteRenderer == null || doorSpriteRenderer.sprite == null)
            {
                return false;
            }

            Sprite sprite = doorSpriteRenderer.sprite;
            Rect textureRect = sprite.textureRect;
            Vector2 pivot = sprite.pivot;
            float pixelsPerUnit = Mathf.Max(1.0f, sprite.pixelsPerUnit);
            Vector3[] worldCorners =
            {
                new Vector3(shadowBounds.min.x, shadowBounds.min.y, doorSpriteRenderer.transform.position.z),
                new Vector3(shadowBounds.min.x, shadowBounds.max.y, doorSpriteRenderer.transform.position.z),
                new Vector3(shadowBounds.max.x, shadowBounds.min.y, doorSpriteRenderer.transform.position.z),
                new Vector3(shadowBounds.max.x, shadowBounds.max.y, doorSpriteRenderer.transform.position.z)
            };

            float minPixelX = float.PositiveInfinity;
            float minPixelY = float.PositiveInfinity;
            float maxPixelX = float.NegativeInfinity;
            float maxPixelY = float.NegativeInfinity;

            for (int i = 0; i < worldCorners.Length; i++)
            {
                Vector3 doorLocal = doorSpriteRenderer.transform.InverseTransformPoint(worldCorners[i]);
                float pixelX = textureRect.x + pivot.x + (doorLocal.x * pixelsPerUnit);
                float pixelY = textureRect.y + pivot.y + (doorLocal.y * pixelsPerUnit);
                minPixelX = Mathf.Min(minPixelX, pixelX);
                minPixelY = Mathf.Min(minPixelY, pixelY);
                maxPixelX = Mathf.Max(maxPixelX, pixelX);
                maxPixelY = Mathf.Max(maxPixelY, pixelY);
            }

            int xMin = Mathf.Clamp(Mathf.FloorToInt(minPixelX), Mathf.FloorToInt(textureRect.xMin), Mathf.CeilToInt(textureRect.xMax));
            int yMin = Mathf.Clamp(Mathf.FloorToInt(minPixelY), Mathf.FloorToInt(textureRect.yMin), Mathf.CeilToInt(textureRect.yMax));
            int xMax = Mathf.Clamp(Mathf.CeilToInt(maxPixelX), Mathf.FloorToInt(textureRect.xMin), Mathf.CeilToInt(textureRect.xMax));
            int yMax = Mathf.Clamp(Mathf.CeilToInt(maxPixelY), Mathf.FloorToInt(textureRect.yMin), Mathf.CeilToInt(textureRect.yMax));

            if (xMax <= xMin || yMax <= yMin)
            {
                return false;
            }

            pixelRect = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            return true;
        }

        private bool TryGetDoorPixels(Texture2D doorTexture, out Color32[] pixels)
        {
            if (cachedDoorTexture == doorTexture && cachedDoorPixels != null)
            {
                pixels = cachedDoorPixels;
                return true;
            }

            try
            {
                cachedDoorPixels = doorTexture.GetPixels32();
                cachedDoorTexture = doorTexture;
                pixels = cachedDoorPixels;
                return true;
            }
            catch (UnityException)
            {
                Debug.LogWarning("Mission4DoorTransition: door texture must be readable for silhouette contact.");
                pixels = null;
                return false;
            }
        }

        private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer current = renderers[i];
                if (current == null || !current.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = current.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(current.bounds);
                }
            }

            return hasBounds;
        }

        private static bool IsPointInsideMeshXY(Vector3 point, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                if (IsPointInTriangleXY(point, a, b, c))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointInTriangleXY(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            float denominator = ((b.y - c.y) * (a.x - c.x)) + ((c.x - b.x) * (a.y - c.y));
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                return false;
            }

            float u = (((b.y - c.y) * (point.x - c.x)) + ((c.x - b.x) * (point.y - c.y))) / denominator;
            float v = (((c.y - a.y) * (point.x - c.x)) + ((a.x - c.x) * (point.y - c.y))) / denominator;
            float w = 1.0f - u - v;
            const float epsilon = -0.0001f;
            return u >= epsilon && v >= epsilon && w >= epsilon;
        }

        private static bool HasEnabledRenderer(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer current = renderers[i];
                if (current != null && current.enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsXY(Bounds a, Bounds b)
        {
            return a.min.x <= b.max.x &&
                a.max.x >= b.min.x &&
                a.min.y <= b.max.y &&
                a.max.y >= b.min.y;
        }
    }
}
