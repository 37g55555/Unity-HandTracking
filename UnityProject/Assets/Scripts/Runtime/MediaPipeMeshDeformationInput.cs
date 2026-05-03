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

        [SerializeField] private ShadowDeformer targetDeformer;
        [SerializeField] private HandLandmarkUdpReceiver handReceiver;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private int controllingHandIndex;
        [SerializeField] private float trackedFrameWidth = 1280.0f;
        [SerializeField] private float trackedFrameHeight = 720.0f;

        [Header("Selection")]
        [SerializeField] private float hoverSnapDistanceLocal = 0.3f;
        [SerializeField] private float pointSmoothingSpeed = 10.0f;

        [Header("Grab / Pull")]
        [SerializeField] private float pinchEnterThresholdPixels = 65.0f;
        [SerializeField] private float pinchExitThresholdPixels = 95.0f;
        [SerializeField] private float pullRadius = 0.3f;
        [SerializeField] private float pullStrength = 0.65f;
        [SerializeField] private float fixedDeformationAmountMultiplier = 0.24f;
        [SerializeField] private float maxPullDeltaPerFrame = 0.018f;

        private const float MinAffectedRadiusLocal = 0.12f;
        private const float MaxAffectedRadiusLocal = 0.65f;

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
        public float DeformationAmountMultiplier => fixedDeformationAmountMultiplier;

        public void SetAffectedRadiusLocal(float value)
        {
            pullRadius = Mathf.Clamp(value, MinAffectedRadiusLocal, MaxAffectedRadiusLocal);
        }

        public void Configure(ShadowDeformer deformer, HandLandmarkUdpReceiver receiver)
        {
            targetDeformer = deformer;
            handReceiver = receiver;
            ApplyComfortTuning();
        }

        private void ApplyComfortTuning()
        {
            hoverSnapDistanceLocal = 0.3f;
            pointSmoothingSpeed = 10.0f;
            SetAffectedRadiusLocal(0.3f);
            pullStrength = 0.65f;
            maxPullDeltaPerFrame = 0.018f;
            fixedDeformationAmountMultiplier = 0.24f;
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            ResolveDependencies();
            if (targetDeformer == null || handReceiver == null || !targetDeformer.HasMesh)
            {
                ResetGestureState();
                return;
            }

            if (!handReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                ResetGestureState();
                return;
            }

            if (!TryGetHandPoint(landmarks, controllingHandIndex, ThumbTipIndex, out Vector2 thumbTracked) ||
                !TryGetHandPoint(landmarks, controllingHandIndex, IndexTipIndex, out Vector2 indexTracked))
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
                ? pinchDistancePixels <= pinchExitThresholdPixels
                : pinchDistancePixels <= pinchEnterThresholdPixels;

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
                targetDeformer.TryGetBoundaryVertexAtBoundaryIndex(
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
                    Vector2 pullDelta = Vector2.ClampMagnitude(GrabLocalPoint - previousGrabLocal, maxPullDeltaPerFrame);
                    if (pullDelta.sqrMagnitude > 0.0f)
                    {
                        targetDeformer.ApplyPull(
                            ActiveBoundaryLocalPoint,
                            pullDelta,
                            pullRadius,
                            pullStrength * fixedDeformationAmountMultiplier);
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

            if (!targetDeformer.TryGetNearestBoundaryVertex(
                    localPoint,
                    out int boundaryArrayIndex,
                    out _,
                    out Vector2 boundaryLocal,
                    out Vector3 boundaryWorld))
            {
                return false;
            }

            float distance = Vector2.Distance(localPoint, boundaryLocal);
            if (distance > hoverSnapDistanceLocal)
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

            float blend = 1.0f - Mathf.Exp(-pointSmoothingSpeed * Time.deltaTime);
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

            Camera camera = ResolveCamera();
            if (camera == null || trackedFrameWidth <= 0.0f || trackedFrameHeight <= 0.0f)
            {
                return false;
            }

            Vector3 viewportPoint = new Vector3(
                Mathf.Clamp01(trackedPoint.x / trackedFrameWidth),
                Mathf.Clamp01(trackedPoint.y / trackedFrameHeight),
                0.0f);

            Ray ray = camera.ViewportPointToRay(viewportPoint);
            Plane meshPlane = new Plane(targetDeformer.transform.forward, targetDeformer.transform.position);
            if (!meshPlane.Raycast(ray, out float enter))
            {
                return false;
            }

            Vector3 worldPoint = ray.GetPoint(enter);
            Vector3 localPoint3 = targetDeformer.transform.InverseTransformPoint(worldPoint);
            localPoint = new Vector2(localPoint3.x, localPoint3.y);
            return true;
        }

        private Vector3 LocalToWorld(Vector2 localPoint)
        {
            return targetDeformer.transform.TransformPoint(new Vector3(localPoint.x, localPoint.y, 0.0f));
        }

        private Camera ResolveCamera()
        {
            if (targetCamera != null)
            {
                return targetCamera;
            }

            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            }

            return targetCamera;
        }

        private void ResolveDependencies()
        {
            if (targetDeformer == null)
            {
                targetDeformer = UnityEngine.Object.FindAnyObjectByType<ShadowDeformer>();
            }

            if (handReceiver == null)
            {
                handReceiver = UnityEngine.Object.FindAnyObjectByType<HandLandmarkUdpReceiver>();
            }

            ResolveCamera();
        }
    }
}
