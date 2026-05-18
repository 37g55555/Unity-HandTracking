using UnityEngine;

namespace ShadowPrototype
{
    public class MediaPipeMeshDeformationInput : MonoBehaviour
    {
        public enum InteractionMode
        {
            None,
            Hover,
            Pull
        }

        private const int LandmarksPerHand = 21;
        private const int ThumbTipIndex = 4;
        private const int IndexTipIndex = 8;
        private const int ControllingHandIndex = 0;
        private const float TrackedFrameWidth = 640.0f;
        private const float TrackedFrameHeight = 480.0f;
        private const float HoverSnapDistanceLocal = 0.35f;
        private const float PointSmoothingSpeed = 16.0f;
        private const float PinchEnterThresholdPixels = 65.0f;
        private const float PinchExitThresholdPixels = 95.0f;
        private const float PullStrength = 1.0f;
        private const float FixedDeformationAmountMultiplier = 0.24f;
        private const float MaxPullDeltaPerFrame = 0.045f;

        [SerializeField] private ShadowMeshDeformer targetMeshDeformer;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private Camera targetCamera;

        private const float MinAffectedRadiusLocal = 0.12f;
        private const float MaxAffectedRadiusLocal = 0.65f;

        private float pullRadius = 0.22f;
        private bool hasSmoothedPoints;
        private Vector2 smoothedThumbLocal;
        private Vector2 smoothedIndexLocal;
        private Vector2 smoothedGrabLocal;

        private bool isGrabLocked;
        private int lockedBoundaryArrayIndex = -1;
        private bool hasPreviousGrabPoint;
        private Vector2 previousGrabLocal;

        public InteractionMode CurrentMode { get; private set; }
        public bool HasProjectedPoints { get; private set; }
        public Vector2 ThumbLocalPoint { get; private set; }
        public Vector2 IndexLocalPoint { get; private set; }
        public Vector2 GrabLocalPoint { get; private set; }
        public Vector3 ThumbWorldPoint { get; private set; }
        public Vector3 IndexWorldPoint { get; private set; }
        public Vector3 GrabWorldPoint { get; private set; }

        public bool HasActiveBoundaryTarget { get; private set; }
        public int ActiveBoundaryArrayIndex { get; private set; } = -1;
        public Vector2 ActiveBoundaryLocalPoint { get; private set; }
        public Vector3 ActiveBoundaryWorldPoint { get; private set; }
        public bool IsGrabLocked => isGrabLocked;
        public float PullRadiusLocal => pullRadius;
        public float AffectedRadiusLocal => pullRadius;
        public float DeformationAmountMultiplier => FixedDeformationAmountMultiplier;

        public void SetAffectedRadiusLocal(float value)
        {
            pullRadius = Mathf.Clamp(value, MinAffectedRadiusLocal, MaxAffectedRadiusLocal);
        }

        private void Update()
        {
            if (targetMeshDeformer == null || mediaPipeReceiver == null || !targetMeshDeformer.HasMesh)
            {
                ResetGestureState();
                return;
            }

            if (!mediaPipeReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                ResetGestureState();
                return;
            }

            if (!TryGetHandPoint(landmarks, ControllingHandIndex, ThumbTipIndex, out Vector2 thumbTracked) ||
                !TryGetHandPoint(landmarks, ControllingHandIndex, IndexTipIndex, out Vector2 indexTracked))
            {
                ResetGestureState();
                return;
            }

            if (!TryProjectTrackedPointToLocal(thumbTracked, out Vector2 thumbLocalRaw) ||
                !TryProjectTrackedPointToLocal(indexTracked, out Vector2 indexLocalRaw))
            {
                ResetGestureState();
                return;
            }

            Vector2 grabLocalRaw = (thumbLocalRaw + indexLocalRaw) * 0.5f;
            SmoothInteractionPoints(thumbLocalRaw, indexLocalRaw, grabLocalRaw);

            ThumbLocalPoint = smoothedThumbLocal;
            IndexLocalPoint = smoothedIndexLocal;
            GrabLocalPoint = smoothedGrabLocal;
            ThumbWorldPoint = LocalToWorld(ThumbLocalPoint);
            IndexWorldPoint = LocalToWorld(IndexLocalPoint);
            GrabWorldPoint = LocalToWorld(GrabLocalPoint);
            HasProjectedPoints = true;

            float pinchDistancePixels = Vector2.Distance(thumbTracked, indexTracked);
            bool isPinching = isGrabLocked
                ? pinchDistancePixels <= PinchExitThresholdPixels
                : pinchDistancePixels <= PinchEnterThresholdPixels;

            bool hasHoverTarget = TryResolveHoverBoundary(IndexLocalPoint);

            if (!isPinching)
            {
                ReleaseGrab();
            }
            else if (!isGrabLocked && hasHoverTarget)
            {
                isGrabLocked = true;
                lockedBoundaryArrayIndex = ActiveBoundaryArrayIndex;
                previousGrabLocal = GrabLocalPoint;
                hasPreviousGrabPoint = false;
            }

            if (isGrabLocked &&
                    targetMeshDeformer.TryGetBoundaryVertexAtBoundaryIndex(
                    lockedBoundaryArrayIndex,
                    out _,
                    out Vector2 lockedBoundaryLocal,
                    out Vector3 lockedBoundaryWorld))
            {
                HasActiveBoundaryTarget = true;
                ActiveBoundaryArrayIndex = lockedBoundaryArrayIndex;
                ActiveBoundaryLocalPoint = lockedBoundaryLocal;
                ActiveBoundaryWorldPoint = lockedBoundaryWorld;
                CurrentMode = InteractionMode.Pull;

                if (hasPreviousGrabPoint)
                {
                    Vector2 pullDelta = Vector2.ClampMagnitude(GrabLocalPoint - previousGrabLocal, MaxPullDeltaPerFrame);
                    if (pullDelta.sqrMagnitude > 0.0f)
                    {
                        targetMeshDeformer.ApplyPull(
                            ActiveBoundaryLocalPoint,
                            pullDelta,
                            pullRadius,
                            PullStrength * FixedDeformationAmountMultiplier);
                    }
                }

                previousGrabLocal = GrabLocalPoint;
                hasPreviousGrabPoint = true;
                return;
            }

            if (hasHoverTarget)
            {
                CurrentMode = InteractionMode.Hover;
                hasPreviousGrabPoint = false;
                return;
            }

            CurrentMode = InteractionMode.None;
            HasActiveBoundaryTarget = false;
            ActiveBoundaryArrayIndex = -1;
            hasPreviousGrabPoint = false;
        }

        private bool TryResolveHoverBoundary(Vector2 localPoint)
        {
            HasActiveBoundaryTarget = false;
            ActiveBoundaryArrayIndex = -1;

            if (!targetMeshDeformer.TryGetNearestBoundaryVertex(
                    localPoint,
                    out int boundaryArrayIndex,
                    out _,
                    out Vector2 boundaryLocal,
                    out Vector3 boundaryWorld))
            {
                return false;
            }

            float distance = Vector2.Distance(localPoint, boundaryLocal);
            if (distance > HoverSnapDistanceLocal)
            {
                return false;
            }

            HasActiveBoundaryTarget = true;
            ActiveBoundaryArrayIndex = boundaryArrayIndex;
            ActiveBoundaryLocalPoint = boundaryLocal;
            ActiveBoundaryWorldPoint = boundaryWorld;
            return true;
        }

        private void SmoothInteractionPoints(Vector2 thumbLocalRaw, Vector2 indexLocalRaw, Vector2 grabLocalRaw)
        {
            if (!hasSmoothedPoints)
            {
                smoothedThumbLocal = thumbLocalRaw;
                smoothedIndexLocal = indexLocalRaw;
                smoothedGrabLocal = grabLocalRaw;
                hasSmoothedPoints = true;
                return;
            }

            float blend = 1.0f - Mathf.Exp(-PointSmoothingSpeed * Time.deltaTime);
            smoothedThumbLocal = Vector2.Lerp(smoothedThumbLocal, thumbLocalRaw, blend);
            smoothedIndexLocal = Vector2.Lerp(smoothedIndexLocal, indexLocalRaw, blend);
            smoothedGrabLocal = Vector2.Lerp(smoothedGrabLocal, grabLocalRaw, blend);
        }

        private void ReleaseGrab()
        {
            isGrabLocked = false;
            lockedBoundaryArrayIndex = -1;
            hasPreviousGrabPoint = false;
        }

        private void ResetGestureState()
        {
            CurrentMode = InteractionMode.None;
            HasProjectedPoints = false;
            HasActiveBoundaryTarget = false;
            ActiveBoundaryArrayIndex = -1;
            hasSmoothedPoints = false;
            ReleaseGrab();
        }

        private bool TryGetHandPoint(Vector3[] landmarks, int handIndex, int landmarkIndex, out Vector2 trackedPoint)
        {
            trackedPoint = Vector2.zero;

            int startIndex = handIndex * LandmarksPerHand;
            int absoluteIndex = startIndex + landmarkIndex;
            if (landmarks == null || absoluteIndex < 0 || absoluteIndex >= landmarks.Length)
            {
                return false;
            }

            Vector3 landmark = landmarks[absoluteIndex];
            trackedPoint = new Vector2(landmark.x, landmark.y);
            return true;
        }

        private bool TryProjectTrackedPointToLocal(Vector2 trackedPoint, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            if (targetCamera == null)
            {
                return false;
            }

            Vector3 viewportPoint = new Vector3(
                Mathf.Clamp01(trackedPoint.x / TrackedFrameWidth),
                Mathf.Clamp01(trackedPoint.y / TrackedFrameHeight),
                0.0f);

            Ray ray = targetCamera.ViewportPointToRay(viewportPoint);
            Plane meshPlane = new Plane(targetMeshDeformer.transform.forward, targetMeshDeformer.transform.position);
            if (!meshPlane.Raycast(ray, out float enter))
            {
                return false;
            }

            Vector3 worldPoint = ray.GetPoint(enter);
            Vector3 localPoint3 = targetMeshDeformer.transform.InverseTransformPoint(worldPoint);
            localPoint = new Vector2(localPoint3.x, localPoint3.y);
            return true;
        }

        private Vector3 LocalToWorld(Vector2 localPoint)
        {
            return targetMeshDeformer.transform.TransformPoint(new Vector3(localPoint.x, localPoint.y, 0.0f));
        }

    }
}
