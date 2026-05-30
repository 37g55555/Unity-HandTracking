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

        public void SetCapturedOverlay(
            Vector2 centerPixels,
            float captureScalePixels,
            Vector2 frameSizePixels,
            Camera overlayCamera,
            Vector2 meshLocalScale)
        {
            if (overlayCamera == null ||
                !overlayCamera.orthographic ||
                captureScalePixels <= 0.0f ||
                meshLocalScale.x <= 0.0f ||
                meshLocalScale.y <= 0.0f ||
                frameSizePixels.x <= 0.0f ||
                frameSizePixels.y <= 0.0f)
            {
                return;
            }

            Vector2 normalizedFramePoint = new Vector2(
                Mathf.Clamp01(centerPixels.x / frameSizePixels.x),
                Mathf.Clamp01(1.0f - (centerPixels.y / frameSizePixels.y)));

            float planeDistance = Mathf.Abs(Vector3.Dot(
                transform.position - overlayCamera.transform.position,
                overlayCamera.transform.forward));
            planeDistance = Mathf.Max(overlayCamera.nearClipPlane, planeDistance);
            Vector3 worldPoint = overlayCamera.ViewportToWorldPoint(
                new Vector3(normalizedFramePoint.x, normalizedFramePoint.y, planeDistance));
            Vector3 targetLocalPosition = transform.parent == null
                ? worldPoint
                : transform.parent.InverseTransformPoint(worldPoint);

            Vector2 cameraWorldSize = new Vector2(
                overlayCamera.orthographicSize * 2.0f * overlayCamera.aspect,
                overlayCamera.orthographicSize * 2.0f);
            Vector2 worldUnitsPerFramePixel = new Vector2(
                cameraWorldSize.x / frameSizePixels.x,
                cameraWorldSize.y / frameSizePixels.y);
            Vector2 rootScale = new Vector2(
                (worldUnitsPerFramePixel.x * captureScalePixels) / meshLocalScale.x,
                (worldUnitsPerFramePixel.y * captureScalePixels) / meshLocalScale.y);

            transform.localPosition = new Vector3(targetLocalPosition.x, targetLocalPosition.y, transform.localPosition.z);
            transform.localScale = new Vector3(rootScale.x, rootScale.y, transform.localScale.z);
            normalizedPosition = normalizedFramePoint;
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
