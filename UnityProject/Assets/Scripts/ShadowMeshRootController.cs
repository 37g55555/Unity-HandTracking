using System.Collections;
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

        [Header("Centering")]
        [SerializeField, Min(0.0f)] private float holdBeforeMoveToOriginSeconds = 2.0f;
        [SerializeField, Min(0.0f)] private float moveToOriginDurationSeconds = 2.0f;

        private float normalizedScale = 0.5f;
        private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        private float normalizedRotation = 0.5f;

        public float CurrentNormalizedScale => normalizedScale;
        public Vector2 CurrentNormalizedPosition => normalizedPosition;
        public float CurrentNormalizedRotation => normalizedRotation;
        public float HoldBeforeMoveToOriginSeconds => holdBeforeMoveToOriginSeconds;
        public float MoveToOriginDurationSeconds => moveToOriginDurationSeconds;

        private void Awake()
        {
            ApplyTransform(preserveLocalPosition: true);
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

        public IEnumerator MoveToOriginCoroutine()
        {
            float holdSeconds = Mathf.Max(0.0f, holdBeforeMoveToOriginSeconds);
            if (holdSeconds > 0.0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            Vector3 startPosition = transform.localPosition;
            Vector3 targetPosition = new Vector3(0.0f, 0.0f, startPosition.z);
            float duration = Mathf.Max(0.0f, moveToOriginDurationSeconds);

            if (duration <= 0.0f)
            {
                transform.localPosition = targetPosition;
                normalizedPosition = new Vector2(0.5f, 0.5f);
                yield break;
            }

            float elapsedSeconds = 0.0f;
            while (elapsedSeconds < duration)
            {
                elapsedSeconds += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedSeconds / duration);
                float easedT = Mathf.SmoothStep(0.0f, 1.0f, t);
                transform.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, easedT);
                yield return null;
            }

            transform.localPosition = targetPosition;
            normalizedPosition = new Vector2(0.5f, 0.5f);
        }

        private void ApplyTransform(bool preserveLocalPosition = false)
        {
            float uniformScale = Mathf.Lerp(MinScale, MaxScale, normalizedScale);
            float localX = Mathf.Lerp(MinLocalPosition.x, MaxLocalPosition.x, normalizedPosition.x);
            float localY = Mathf.Lerp(MinLocalPosition.y, MaxLocalPosition.y, normalizedPosition.y);
            float rotationZ = Mathf.Lerp(MinRotationZ, MaxRotationZ, normalizedRotation);

            transform.localScale = Vector3.one * uniformScale;
            if (!preserveLocalPosition)
            {
                transform.localPosition = new Vector3(localX, localY, transform.localPosition.z);
            }

            transform.localRotation = Quaternion.Euler(0.0f, 0.0f, rotationZ);
        }
    }
}
