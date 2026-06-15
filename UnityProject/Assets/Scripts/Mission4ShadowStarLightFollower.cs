using UnityEngine;

namespace ShadowPrototype
{
    public sealed class Mission4ShadowStarLightFollower : MonoBehaviour
    {
        [SerializeField] private Transform lightTransform;
        [SerializeField] private ArucoMarkerFollower lightMarkerFollower;
        [SerializeField] private string fallbackLightName = "SoftWhiteCircleMarkerObject";
        [SerializeField] private bool waitForFirstMarkerPose = true;
        [SerializeField, Min(0.0f)] private float followSmoothing = 4.5f;
        [SerializeField] private Vector3 worldOffset;
        [SerializeField] private bool keepInitialZ = true;

        private float initialZ;

        private void Awake()
        {
            initialZ = transform.position.z;
            ResolveLight();
        }

        private void LateUpdate()
        {
            ResolveLight();
            if (lightTransform == null)
            {
                return;
            }

            if (waitForFirstMarkerPose && lightMarkerFollower != null && !lightMarkerFollower.HasReceivedPose)
            {
                return;
            }

            Vector3 targetPosition = lightTransform.position + worldOffset;
            if (keepInitialZ)
            {
                targetPosition.z = initialZ;
            }

            float blend = GetFrameBlend(followSmoothing);
            transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
        }

        private void ResolveLight()
        {
            if (lightTransform != null)
            {
                if (lightMarkerFollower == null)
                {
                    lightMarkerFollower = lightTransform.GetComponent<ArucoMarkerFollower>();
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(fallbackLightName))
            {
                return;
            }

            GameObject lightObject = GameObject.Find(fallbackLightName);
            lightTransform = lightObject != null ? lightObject.transform : null;
            lightMarkerFollower = lightObject != null ? lightObject.GetComponent<ArucoMarkerFollower>() : null;
        }

        private static float GetFrameBlend(float speed)
        {
            if (speed <= 0.0f)
            {
                return 1.0f;
            }

            return 1.0f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        }
    }
}
