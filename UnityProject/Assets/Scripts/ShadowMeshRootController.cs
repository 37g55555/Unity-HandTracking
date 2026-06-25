using UnityEngine;

namespace ShadowPrototype
{
    public class ShadowMeshRootController : MonoBehaviour
    {
        private const float MinScale = 0.5f;
        private const float MaxScale = 2.0f;
        private const float MinRotationZ = -55.0f;
        private const float MaxRotationZ = 55.0f;

        [SerializeField, Min(0.01f)] private float capturedOverlayInitialScaleMultiplier = 1.0f;
        [SerializeField] private Vector2 capturedOverlayInitialPositionOffset = Vector2.zero;

        private void Awake()
        {
            float uniformScale = Mathf.Lerp(MinScale, MaxScale, 0.5f);
            float rotationZ = Mathf.Lerp(MinRotationZ, MaxRotationZ, 0.5f);
            transform.localScale = Vector3.one * uniformScale;
            transform.localRotation = Quaternion.Euler(0.0f, 0.0f, rotationZ);
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
            Rect sourceFrameViewportRect = GetAspectFitViewportRect(frameSizePixels, overlayCamera);
            Vector2 normalizedViewportPoint = new Vector2(
                sourceFrameViewportRect.x + (sourceFrameViewportRect.width * normalizedFramePoint.x),
                sourceFrameViewportRect.y + (sourceFrameViewportRect.height * normalizedFramePoint.y));

            float planeDistance = Mathf.Abs(Vector3.Dot(
                transform.position - overlayCamera.transform.position,
                overlayCamera.transform.forward));
            planeDistance = Mathf.Max(overlayCamera.nearClipPlane, planeDistance);
            Vector3 worldPoint = overlayCamera.ViewportToWorldPoint(
                new Vector3(normalizedViewportPoint.x, normalizedViewportPoint.y, planeDistance));
            Vector3 targetLocalPosition = transform.parent == null
                ? worldPoint
                : transform.parent.InverseTransformPoint(worldPoint);
            targetLocalPosition += new Vector3(
                capturedOverlayInitialPositionOffset.x,
                capturedOverlayInitialPositionOffset.y,
                0.0f);

            Vector2 cameraWorldSize = new Vector2(
                overlayCamera.orthographicSize * 2.0f * overlayCamera.aspect,
                overlayCamera.orthographicSize * 2.0f);
            Vector2 sourceFrameWorldSize = new Vector2(
                cameraWorldSize.x * sourceFrameViewportRect.width,
                cameraWorldSize.y * sourceFrameViewportRect.height);
            Vector2 worldUnitsPerFramePixel = new Vector2(
                sourceFrameWorldSize.x / frameSizePixels.x,
                sourceFrameWorldSize.y / frameSizePixels.y);
            Vector2 rootScale = new Vector2(
                (worldUnitsPerFramePixel.x * captureScalePixels) / meshLocalScale.x,
                (worldUnitsPerFramePixel.y * captureScalePixels) / meshLocalScale.y);
            rootScale *= Mathf.Max(0.01f, capturedOverlayInitialScaleMultiplier);

            transform.localPosition = new Vector3(targetLocalPosition.x, targetLocalPosition.y, transform.localPosition.z);
            transform.localScale = new Vector3(rootScale.x, rootScale.y, transform.localScale.z);
        }

        private static Rect GetAspectFitViewportRect(Vector2 frameSizePixels, Camera overlayCamera)
        {
            float frameAspect = frameSizePixels.x / frameSizePixels.y;
            float cameraAspect = Mathf.Max(0.0001f, overlayCamera.aspect);

            if (frameAspect > cameraAspect)
            {
                float height = cameraAspect / frameAspect;
                return new Rect(0.0f, (1.0f - height) * 0.5f, 1.0f, height);
            }

            float width = frameAspect / cameraAspect;
            return new Rect((1.0f - width) * 0.5f, 0.0f, width, 1.0f);
        }

        public void CenterMeshInCamera(ShadowMeshDeformer meshDeformer, Camera overlayCamera)
        {
            if (meshDeformer == null || overlayCamera == null || !overlayCamera.orthographic || !meshDeformer.HasMesh)
            {
                return;
            }

            Bounds meshBounds = meshDeformer.GetWorldBounds();
            float planeDistance = Mathf.Abs(Vector3.Dot(
                meshBounds.center - overlayCamera.transform.position,
                overlayCamera.transform.forward));
            planeDistance = Mathf.Max(overlayCamera.nearClipPlane, planeDistance);

            Vector3 cameraCenter = overlayCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, planeDistance));
            Vector3 offset = cameraCenter - meshBounds.center;
            transform.position += offset;
        }
    }
}
