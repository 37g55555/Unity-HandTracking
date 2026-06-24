using UnityEngine;

namespace ShadowPrototype
{
    public sealed class TransformFollowTarget : MonoBehaviour
    {
        [SerializeField] private Transform targetTransform;
        [SerializeField, Min(0.0f)] private float followSmoothing = 4.5f;
        [SerializeField] private Vector3 worldOffset;
        [SerializeField] private bool keepInitialZ = true;

        private float initialZ;

        private void Awake()
        {
            initialZ = transform.position.z;
        }

        private void LateUpdate()
        {
            if (targetTransform == null)
            {
                return;
            }

            Vector3 targetPosition = targetTransform.position + worldOffset;
            if (keepInitialZ)
            {
                targetPosition.z = initialZ;
            }

            float blend = GetFrameBlend(followSmoothing);
            transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
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
