using UnityEngine;

namespace ShadowPrototype
{
    public class ShadowMeshRootController : MonoBehaviour
    {
        private const float MinScale = 0.5f;
        private const float MaxScale = 2.0f;
        private static readonly Vector2 MinLocalPosition = new Vector2(-3.0f, -1.75f);
        private static readonly Vector2 MaxLocalPosition = new Vector2(3.0f, 1.75f);
        private const float MinRotationZ = -55.0f;
        private const float MaxRotationZ = 55.0f;

        private float normalizedScale = 0.5f;
        private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        private float normalizedRotation = 0.5f;

        public float CurrentNormalizedScale => normalizedScale;
        public Vector2 CurrentNormalizedPosition => normalizedPosition;
        public float CurrentNormalizedRotation => normalizedRotation;

        private void Awake()
        {
            ApplyTransform();
        }

        public void SetScaleNormalized(float t)
        {
            normalizedScale = Mathf.Clamp01(t);
            ApplyTransform();
        }

        public void SetPositionNormalized(Vector2 t)
        {
            normalizedPosition = new Vector2(Mathf.Clamp01(t.x), Mathf.Clamp01(t.y));
            ApplyTransform();
        }

        public void SetRotationNormalized(float t)
        {
            normalizedRotation = Mathf.Clamp01(t);
            ApplyTransform();
        }

        public void SetPoseNormalized(Vector2 position, float scale, float rotation)
        {
            normalizedPosition = new Vector2(Mathf.Clamp01(position.x), Mathf.Clamp01(position.y));
            normalizedScale = Mathf.Clamp01(scale);
            normalizedRotation = Mathf.Clamp01(rotation);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            float uniformScale = Mathf.Lerp(MinScale, MaxScale, normalizedScale);
            float localX = Mathf.Lerp(MinLocalPosition.x, MaxLocalPosition.x, normalizedPosition.x);
            float localY = Mathf.Lerp(MinLocalPosition.y, MaxLocalPosition.y, normalizedPosition.y);
            float rotationZ = Mathf.Lerp(MinRotationZ, MaxRotationZ, normalizedRotation);

            transform.localScale = Vector3.one * uniformScale;
            transform.localPosition = new Vector3(localX, localY, transform.localPosition.z);
            transform.localRotation = Quaternion.Euler(0.0f, 0.0f, rotationZ);
        }
    }
}
