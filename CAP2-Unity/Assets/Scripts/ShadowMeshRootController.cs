using UnityEngine;

namespace ShadowPrototype
{
    public class ShadowMeshRootController : MonoBehaviour
    {
        [Header("Scale")]
        [SerializeField] private float minScale = 0.5f;
        [SerializeField] private float maxScale = 2.0f;
        [SerializeField, Range(0f, 1f)] private float normalizedScale = 0.5f;

        [Header("Position")]
        [SerializeField] private Vector2 minLocalPosition = new Vector2(-3.0f, -1.75f);
        [SerializeField] private Vector2 maxLocalPosition = new Vector2(3.0f, 1.75f);
        [SerializeField] private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);

        [Header("Rotation")]
        [SerializeField] private float minRotationZ = -55.0f;
        [SerializeField] private float maxRotationZ = 55.0f;
        [SerializeField, Range(0f, 1f)] private float normalizedRotation = 0.5f;

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
            float uniformScale = Mathf.Lerp(minScale, maxScale, normalizedScale);
            float localX = Mathf.Lerp(minLocalPosition.x, maxLocalPosition.x, normalizedPosition.x);
            float localY = Mathf.Lerp(minLocalPosition.y, maxLocalPosition.y, normalizedPosition.y);
            float rotationZ = Mathf.Lerp(minRotationZ, maxRotationZ, normalizedRotation);

            transform.localScale = Vector3.one * uniformScale;
            transform.localPosition = new Vector3(localX, localY, transform.localPosition.z);
            transform.localRotation = Quaternion.Euler(0.0f, 0.0f, rotationZ);
        }
    }
}
