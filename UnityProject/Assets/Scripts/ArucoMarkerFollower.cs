using UnityEngine;

namespace ShadowPrototype
{
    public sealed class ArucoMarkerFollower : MonoBehaviour
    {
        [Header("Marker")]
        [SerializeField] private string markerDictionary = "DICT_4X4_50";
        [SerializeField] private int markerId;

        [Header("References")]
        [SerializeField] private Transform markerPoseSource;
        [SerializeField] private Camera targetCamera;

        [Header("Follow")]
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation;
        [SerializeField, Min(0.0f)] private float followSmoothing = 18.0f;
        [SerializeField, Min(0.0f)] private float rotationSmoothing = 18.0f;
        [SerializeField] private Vector3 worldOffset;
        [SerializeField] private float followPlaneZ;
        [SerializeField] private bool startHiddenUntilFirstMarker = true;
        [SerializeField] private bool hideWhenMarkerLost;
        [SerializeField, Min(0.0f)] private float markerLostTimeoutSeconds = 0.35f;

        private Renderer[] cachedRenderers;
        private bool hasExternalPose;
        private bool hasReceivedPose;
        private bool isVisible;
        private Vector3 externalWorldPosition;
        private Quaternion externalWorldRotation = Quaternion.identity;
        private float lastSeenTime = -999.0f;

        public string MarkerDictionary => markerDictionary;
        public int MarkerId => markerId;
        public bool HasReceivedPose => hasReceivedPose;
        public bool IsVisible => isVisible;

        private void Awake()
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            SetVisible(!startHiddenUntilFirstMarker && !hideWhenMarkerLost);
        }

        private void LateUpdate()
        {
            if (markerPoseSource != null)
            {
                lastSeenTime = Time.unscaledTime;
                if (!hasReceivedPose || !isVisible)
                {
                    ApplyPoseImmediate(markerPoseSource.position, markerPoseSource.rotation);
                    hasReceivedPose = true;
                }
                else
                {
                    Follow(markerPoseSource.position, markerPoseSource.rotation);
                }

                SetVisible(true);
                return;
            }

            if (hasExternalPose && Time.unscaledTime - lastSeenTime <= markerLostTimeoutSeconds)
            {
                Follow(externalWorldPosition, externalWorldRotation);
                SetVisible(true);
                return;
            }

            if (hideWhenMarkerLost)
            {
                SetVisible(false);
            }
        }

        public void SetMarkerWorldPose(string dictionary, int id, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!MatchesMarker(dictionary, id))
            {
                return;
            }

            externalWorldPosition = worldPosition;
            externalWorldRotation = worldRotation;
            hasExternalPose = true;
            lastSeenTime = Time.unscaledTime;

            if (!hasReceivedPose || !isVisible)
            {
                ApplyPoseImmediate(worldPosition, worldRotation);
                hasReceivedPose = true;
                SetVisible(true);
            }
        }

        public void SetMarkerWorldPose(int id, Vector3 worldPosition, Quaternion worldRotation)
        {
            SetMarkerWorldPose(markerDictionary, id, worldPosition, worldRotation);
        }

        public void SetMarkerViewportPose(string dictionary, int id, Vector2 viewportPosition, float rotationDegrees)
        {
            if (!MatchesMarker(dictionary, id))
            {
                return;
            }

            if (targetCamera == null)
            {
                return;
            }

            Ray ray = targetCamera.ViewportPointToRay(new Vector3(viewportPosition.x, viewportPosition.y, 0.0f));
            Plane followPlane = new Plane(Vector3.forward, new Vector3(0.0f, 0.0f, followPlaneZ));
            if (!followPlane.Raycast(ray, out float distance))
            {
                return;
            }

            SetMarkerWorldPose(dictionary, id, ray.GetPoint(distance), Quaternion.Euler(0.0f, 0.0f, rotationDegrees));
        }

        public void MarkMarkerLost(string dictionary, int id)
        {
            if (!MatchesMarker(dictionary, id))
            {
                return;
            }

            hasExternalPose = false;
        }

        private void Follow(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (followPosition)
            {
                Vector3 targetPosition = worldPosition + worldOffset;
                float blend = GetFrameBlend(followSmoothing);
                transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
            }

            if (followRotation)
            {
                float blend = GetFrameBlend(rotationSmoothing);
                transform.rotation = Quaternion.Slerp(transform.rotation, worldRotation, blend);
            }
        }

        private void ApplyPoseImmediate(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (followPosition)
            {
                transform.position = worldPosition + worldOffset;
            }

            if (followRotation)
            {
                transform.rotation = worldRotation;
            }
        }

        private bool MatchesMarker(string dictionary, int id)
        {
            return id == markerId && NormalizeDictionary(dictionary) == NormalizeDictionary(markerDictionary);
        }

        private static string NormalizeDictionary(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim()
                .ToUpperInvariant()
                .Replace("DICT_", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty);
        }

        private static float GetFrameBlend(float speed)
        {
            if (speed <= 0.0f)
            {
                return 1.0f;
            }

            return 1.0f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;

            if (cachedRenderers == null)
            {
                return;
            }

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                if (cachedRenderers[i] != null)
                {
                    cachedRenderers[i].enabled = visible;
                }
            }
        }
    }
}
