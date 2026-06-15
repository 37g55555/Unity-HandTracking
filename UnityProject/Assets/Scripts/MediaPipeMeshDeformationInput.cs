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

        public struct HandInteractionSnapshot
        {
            public InteractionMode CurrentMode;
            public bool HasProjectedPoints;
            public Vector2 ThumbLocalPoint;
            public Vector2 IndexLocalPoint;
            public Vector2 GrabLocalPoint;
            public Vector3 ThumbWorldPoint;
            public Vector3 IndexWorldPoint;
            public Vector3 GrabWorldPoint;
            public bool HasActiveBoundaryTarget;
            public int ActiveBoundaryArrayIndex;
            public Vector2 ActiveBoundaryLocalPoint;
            public Vector3 ActiveBoundaryWorldPoint;
            public bool IsGrabLocked;
        }

        public const int LandmarksPerHand = 21;
        public const int MaxHands = 2;
        public const float TrackedFrameWidth = 1920.0f;
        public const float TrackedFrameHeight = 1080.0f;

        private const int ThumbTipIndex = 4;
        private const int IndexTipIndex = 8;
        private const float DefaultHoverSnapDistanceLocal = 0.22f;
        private const float PointSmoothingSpeed = 16.0f;
        private const float DefaultPinchEnterThresholdPixels = 38.0f;
        private const float DefaultPinchExitThresholdPixels = 58.0f;
        private const float DefaultGrabActivationHoldSeconds = 0.12f;
        private const float DefaultAffectedRadiusLocal = 0.4f;
        private const float DefaultPullStrength = 1.0f;
        private const float MaxPullDeltaPerFrame = 0.045f;
        private const float MinAffectedRadiusLocal = 0.12f;
        private const float MaxAffectedRadiusLocal = 1.0f;

        [SerializeField] private ShadowMeshDeformer targetMeshDeformer;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private Camera targetCamera;

        [Header("Grab Gesture")]
        [SerializeField] private float hoverSnapDistanceLocal = DefaultHoverSnapDistanceLocal;
        [SerializeField] private float pinchEnterThresholdPixels = DefaultPinchEnterThresholdPixels;
        [SerializeField] private float pinchExitThresholdPixels = DefaultPinchExitThresholdPixels;
        [SerializeField] private float grabActivationHoldSeconds = DefaultGrabActivationHoldSeconds;
        [SerializeField, Range(MinAffectedRadiusLocal, MaxAffectedRadiusLocal)] private float affectedRadiusLocal = DefaultAffectedRadiusLocal;
        [SerializeField, Range(0.0f, 1.0f)] private float pullStrength = DefaultPullStrength;

        private readonly HandInteractionState[] handStates = new HandInteractionState[MaxHands];

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
        public bool IsGrabLocked { get; private set; }
        public float PullRadiusLocal => affectedRadiusLocal;
        public MediaPipeUdpReceiver Receiver => mediaPipeReceiver;

        private void Awake()
        {
            EnsureHandStatesInitialized();
        }

        private void EnsureHandStatesInitialized()
        {
            for (int i = 0; i < handStates.Length; i++)
            {
                if (handStates[i] == null)
                {
                    handStates[i] = new HandInteractionState();
                }
            }
        }

        private void OnValidate()
        {
            affectedRadiusLocal = Mathf.Clamp(affectedRadiusLocal, MinAffectedRadiusLocal, MaxAffectedRadiusLocal);
            pullStrength = Mathf.Clamp01(pullStrength);
        }

        private Camera ResolveTargetCamera()
        {
            if (targetCamera != null && targetCamera.isActiveAndEnabled)
            {
                return targetCamera;
            }

            targetCamera = Camera.main;
            return targetCamera;
        }

        private void ResolveRuntimeReferences()
        {
            if (targetMeshDeformer == null)
            {
                targetMeshDeformer = FindObjectOfType<ShadowMeshDeformer>();
            }

            if (mediaPipeReceiver == null)
            {
                mediaPipeReceiver = GetComponent<MediaPipeUdpReceiver>();
                if (mediaPipeReceiver == null)
                {
                    mediaPipeReceiver = FindObjectOfType<MediaPipeUdpReceiver>();
                }
            }
        }

        public bool TryGetHandInteractionState(int handIndex, out HandInteractionSnapshot snapshot)
        {
            snapshot = default;

            if (handIndex < 0 || handIndex >= handStates.Length)
            {
                return false;
            }

            EnsureHandStatesInitialized();
            HandInteractionState state = handStates[handIndex];
            if (state == null || !state.HasProjectedPoints)
            {
                return false;
            }

            snapshot = CreateSnapshot(state);
            return true;
        }

        private void Update()
        {
            EnsureHandStatesInitialized();
            ResolveRuntimeReferences();

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

            int handCount = Mathf.Min(MaxHands, landmarks.Length / LandmarksPerHand);
            int[] stateToDetectedHand = AssignDetectedHandsToStates(landmarks, handCount);
            bool hasAnyProjectedHand = false;
            int primaryHandIndex = -1;

            for (int handIndex = 0; handIndex < MaxHands; handIndex++)
            {
                HandInteractionState state = handStates[handIndex];
                int detectedHandIndex = stateToDetectedHand[handIndex];
                if (detectedHandIndex < 0 || !UpdateHand(detectedHandIndex, landmarks, state))
                {
                    ResetHandState(state);
                    continue;
                }

                hasAnyProjectedHand = true;
                if (primaryHandIndex < 0 ||
                    (state.CurrentMode == InteractionMode.Pull && handStates[primaryHandIndex].CurrentMode != InteractionMode.Pull))
                {
                    primaryHandIndex = handIndex;
                }
            }

            if (!hasAnyProjectedHand || primaryHandIndex < 0)
            {
                ClearPublishedState();
                return;
            }

            PublishHandState(handStates[primaryHandIndex]);
        }

        private int[] AssignDetectedHandsToStates(Vector3[] landmarks, int handCount)
        {
            int[] assignments = new int[MaxHands];
            bool[] usedDetectedHands = new bool[MaxHands];
            bool[] hasDetectedGrabPoint = new bool[MaxHands];
            Vector2[] detectedGrabPoints = new Vector2[MaxHands];

            for (int i = 0; i < assignments.Length; i++)
            {
                assignments[i] = -1;
            }

            for (int detectedHandIndex = 0; detectedHandIndex < handCount; detectedHandIndex++)
            {
                hasDetectedGrabPoint[detectedHandIndex] = TryGetRawGrabLocal(
                    landmarks,
                    detectedHandIndex,
                    out detectedGrabPoints[detectedHandIndex]);
            }

            AssignTrackedStatesToNearestDetectedHands(
                true,
                handCount,
                assignments,
                usedDetectedHands,
                hasDetectedGrabPoint,
                detectedGrabPoints);

            AssignTrackedStatesToNearestDetectedHands(
                false,
                handCount,
                assignments,
                usedDetectedHands,
                hasDetectedGrabPoint,
                detectedGrabPoints);

            for (int stateIndex = 0; stateIndex < MaxHands; stateIndex++)
            {
                if (assignments[stateIndex] >= 0)
                {
                    continue;
                }

                for (int detectedHandIndex = 0; detectedHandIndex < handCount; detectedHandIndex++)
                {
                    if (usedDetectedHands[detectedHandIndex] || !hasDetectedGrabPoint[detectedHandIndex])
                    {
                        continue;
                    }

                    assignments[stateIndex] = detectedHandIndex;
                    usedDetectedHands[detectedHandIndex] = true;
                    break;
                }
            }

            return assignments;
        }

        private void AssignTrackedStatesToNearestDetectedHands(
            bool requireGrabLocked,
            int handCount,
            int[] assignments,
            bool[] usedDetectedHands,
            bool[] hasDetectedGrabPoint,
            Vector2[] detectedGrabPoints)
        {
            for (int stateIndex = 0; stateIndex < MaxHands; stateIndex++)
            {
                if (assignments[stateIndex] >= 0)
                {
                    continue;
                }

                HandInteractionState state = handStates[stateIndex];
                if (state == null || !state.HasSmoothedPoints || state.IsGrabLocked != requireGrabLocked)
                {
                    continue;
                }

                int bestDetectedHandIndex = -1;
                float bestDistanceSquared = float.PositiveInfinity;
                for (int detectedHandIndex = 0; detectedHandIndex < handCount; detectedHandIndex++)
                {
                    if (usedDetectedHands[detectedHandIndex] || !hasDetectedGrabPoint[detectedHandIndex])
                    {
                        continue;
                    }

                    float distanceSquared = (detectedGrabPoints[detectedHandIndex] - state.SmoothedGrabLocal).sqrMagnitude;
                    if (distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    bestDetectedHandIndex = detectedHandIndex;
                }

                if (bestDetectedHandIndex >= 0)
                {
                    assignments[stateIndex] = bestDetectedHandIndex;
                    usedDetectedHands[bestDetectedHandIndex] = true;
                }
            }
        }

        private bool UpdateHand(int handIndex, Vector3[] landmarks, HandInteractionState state)
        {
            if (!TryGetHandPoint(landmarks, handIndex, ThumbTipIndex, out Vector2 thumbTracked) ||
                !TryGetHandPoint(landmarks, handIndex, IndexTipIndex, out Vector2 indexTracked))
            {
                return false;
            }

            if (!TryProjectTrackedPointToLocal(thumbTracked, out Vector2 thumbLocalRaw) ||
                !TryProjectTrackedPointToLocal(indexTracked, out Vector2 indexLocalRaw))
            {
                return false;
            }

            Vector2 grabLocalRaw = (thumbLocalRaw + indexLocalRaw) * 0.5f;
            SmoothInteractionPoints(state, thumbLocalRaw, indexLocalRaw, grabLocalRaw);

            state.ThumbLocalPoint = state.SmoothedThumbLocal;
            state.IndexLocalPoint = state.SmoothedIndexLocal;
            state.GrabLocalPoint = state.SmoothedGrabLocal;
            state.ThumbWorldPoint = LocalToWorld(state.ThumbLocalPoint);
            state.IndexWorldPoint = LocalToWorld(state.IndexLocalPoint);
            state.GrabWorldPoint = LocalToWorld(state.GrabLocalPoint);
            state.HasProjectedPoints = true;

            float pinchDistancePixels = Vector2.Distance(thumbTracked, indexTracked);
            float enterThreshold = Mathf.Max(1.0f, pinchEnterThresholdPixels);
            float exitThreshold = Mathf.Max(enterThreshold, pinchExitThresholdPixels);
            bool isPinching = state.IsGrabLocked
                ? pinchDistancePixels <= exitThreshold
                : pinchDistancePixels <= enterThreshold;

            bool hasHoverTarget = TryResolveHoverBoundary(
                state.IndexLocalPoint,
                out int boundaryArrayIndex,
                out Vector2 boundaryLocal,
                out Vector3 boundaryWorld);

            if (!isPinching)
            {
                ReleaseGrab(state);
            }
            else if (!state.IsGrabLocked && hasHoverTarget)
            {
                state.PendingGrabSeconds += Time.deltaTime;
                if (state.PendingGrabSeconds >= grabActivationHoldSeconds)
                {
                    state.IsGrabLocked = true;
                    state.LockedBoundaryArrayIndex = boundaryArrayIndex;
                    state.PreviousGrabLocal = state.GrabLocalPoint;
                    state.HasPreviousGrabPoint = false;
                    state.PendingGrabSeconds = 0.0f;
                }
            }
            else if (!state.IsGrabLocked)
            {
                state.PendingGrabSeconds = 0.0f;
            }

            if (state.IsGrabLocked &&
                targetMeshDeformer.TryGetBoundaryVertexAtBoundaryIndex(
                    state.LockedBoundaryArrayIndex,
                    out _,
                    out Vector2 lockedBoundaryLocal,
                    out Vector3 lockedBoundaryWorld))
            {
                state.HasActiveBoundaryTarget = true;
                state.ActiveBoundaryArrayIndex = state.LockedBoundaryArrayIndex;
                state.ActiveBoundaryLocalPoint = lockedBoundaryLocal;
                state.ActiveBoundaryWorldPoint = lockedBoundaryWorld;
                state.CurrentMode = InteractionMode.Pull;

                if (state.HasPreviousGrabPoint)
                {
                    Vector2 pullDelta = Vector2.ClampMagnitude(state.GrabLocalPoint - state.PreviousGrabLocal, MaxPullDeltaPerFrame);
                    if (pullDelta.sqrMagnitude > 0.0f)
                    {
                        targetMeshDeformer.ApplyPull(
                            state.ActiveBoundaryLocalPoint,
                            pullDelta,
                            affectedRadiusLocal,
                            pullStrength);
                    }
                }

                state.PreviousGrabLocal = state.GrabLocalPoint;
                state.HasPreviousGrabPoint = true;
                return true;
            }

            if (hasHoverTarget)
            {
                state.CurrentMode = InteractionMode.Hover;
                state.HasActiveBoundaryTarget = true;
                state.ActiveBoundaryArrayIndex = boundaryArrayIndex;
                state.ActiveBoundaryLocalPoint = boundaryLocal;
                state.ActiveBoundaryWorldPoint = boundaryWorld;
                state.HasPreviousGrabPoint = false;
                return true;
            }

            state.CurrentMode = InteractionMode.None;
            state.HasActiveBoundaryTarget = false;
            state.ActiveBoundaryArrayIndex = -1;
            state.HasPreviousGrabPoint = false;
            return true;
        }

        private bool TryGetRawGrabLocal(Vector3[] landmarks, int handIndex, out Vector2 grabLocalRaw)
        {
            grabLocalRaw = Vector2.zero;

            if (!TryGetHandPoint(landmarks, handIndex, ThumbTipIndex, out Vector2 thumbTracked) ||
                !TryGetHandPoint(landmarks, handIndex, IndexTipIndex, out Vector2 indexTracked))
            {
                return false;
            }

            if (!TryProjectTrackedPointToLocal(thumbTracked, out Vector2 thumbLocalRaw) ||
                !TryProjectTrackedPointToLocal(indexTracked, out Vector2 indexLocalRaw))
            {
                return false;
            }

            grabLocalRaw = (thumbLocalRaw + indexLocalRaw) * 0.5f;
            return true;
        }

        private bool TryResolveHoverBoundary(
            Vector2 localPoint,
            out int boundaryArrayIndex,
            out Vector2 boundaryLocal,
            out Vector3 boundaryWorld)
        {
            boundaryArrayIndex = -1;
            boundaryLocal = Vector2.zero;
            boundaryWorld = Vector3.zero;

            if (!targetMeshDeformer.TryGetNearestBoundaryVertex(
                    localPoint,
                    out int candidateBoundaryArrayIndex,
                    out _,
                    out Vector2 candidateBoundaryLocal,
                    out Vector3 candidateBoundaryWorld))
            {
                return false;
            }

            float distance = Vector2.Distance(localPoint, candidateBoundaryLocal);
            if (distance > hoverSnapDistanceLocal)
            {
                return false;
            }

            boundaryArrayIndex = candidateBoundaryArrayIndex;
            boundaryLocal = candidateBoundaryLocal;
            boundaryWorld = candidateBoundaryWorld;
            return true;
        }

        private void SmoothInteractionPoints(
            HandInteractionState state,
            Vector2 thumbLocalRaw,
            Vector2 indexLocalRaw,
            Vector2 grabLocalRaw)
        {
            if (!state.HasSmoothedPoints)
            {
                state.SmoothedThumbLocal = thumbLocalRaw;
                state.SmoothedIndexLocal = indexLocalRaw;
                state.SmoothedGrabLocal = grabLocalRaw;
                state.HasSmoothedPoints = true;
                return;
            }

            float blend = 1.0f - Mathf.Exp(-PointSmoothingSpeed * Time.deltaTime);
            state.SmoothedThumbLocal = Vector2.Lerp(state.SmoothedThumbLocal, thumbLocalRaw, blend);
            state.SmoothedIndexLocal = Vector2.Lerp(state.SmoothedIndexLocal, indexLocalRaw, blend);
            state.SmoothedGrabLocal = Vector2.Lerp(state.SmoothedGrabLocal, grabLocalRaw, blend);
        }

        private void ReleaseGrab(HandInteractionState state)
        {
            state.IsGrabLocked = false;
            state.LockedBoundaryArrayIndex = -1;
            state.HasPreviousGrabPoint = false;
            state.PendingGrabSeconds = 0.0f;
        }

        private void ResetHandState(HandInteractionState state)
        {
            state.CurrentMode = InteractionMode.None;
            state.HasProjectedPoints = false;
            state.HasActiveBoundaryTarget = false;
            state.ActiveBoundaryArrayIndex = -1;
            state.HasSmoothedPoints = false;
            ReleaseGrab(state);
        }

        private void ResetGestureState()
        {
            for (int i = 0; i < handStates.Length; i++)
            {
                if (handStates[i] == null)
                {
                    handStates[i] = new HandInteractionState();
                }

                ResetHandState(handStates[i]);
            }

            ClearPublishedState();
        }

        private void ClearPublishedState()
        {
            CurrentMode = InteractionMode.None;
            HasProjectedPoints = false;
            HasActiveBoundaryTarget = false;
            ActiveBoundaryArrayIndex = -1;
            IsGrabLocked = false;
        }

        private void PublishHandState(HandInteractionState state)
        {
            CurrentMode = state.CurrentMode;
            HasProjectedPoints = state.HasProjectedPoints;
            ThumbLocalPoint = state.ThumbLocalPoint;
            IndexLocalPoint = state.IndexLocalPoint;
            GrabLocalPoint = state.GrabLocalPoint;
            ThumbWorldPoint = state.ThumbWorldPoint;
            IndexWorldPoint = state.IndexWorldPoint;
            GrabWorldPoint = state.GrabWorldPoint;
            HasActiveBoundaryTarget = state.HasActiveBoundaryTarget;
            ActiveBoundaryArrayIndex = state.ActiveBoundaryArrayIndex;
            ActiveBoundaryLocalPoint = state.ActiveBoundaryLocalPoint;
            ActiveBoundaryWorldPoint = state.ActiveBoundaryWorldPoint;
            IsGrabLocked = state.IsGrabLocked;
        }

        private static HandInteractionSnapshot CreateSnapshot(HandInteractionState state)
        {
            return new HandInteractionSnapshot
            {
                CurrentMode = state.CurrentMode,
                HasProjectedPoints = state.HasProjectedPoints,
                ThumbLocalPoint = state.ThumbLocalPoint,
                IndexLocalPoint = state.IndexLocalPoint,
                GrabLocalPoint = state.GrabLocalPoint,
                ThumbWorldPoint = state.ThumbWorldPoint,
                IndexWorldPoint = state.IndexWorldPoint,
                GrabWorldPoint = state.GrabWorldPoint,
                HasActiveBoundaryTarget = state.HasActiveBoundaryTarget,
                ActiveBoundaryArrayIndex = state.ActiveBoundaryArrayIndex,
                ActiveBoundaryLocalPoint = state.ActiveBoundaryLocalPoint,
                ActiveBoundaryWorldPoint = state.ActiveBoundaryWorldPoint,
                IsGrabLocked = state.IsGrabLocked
            };
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

        public bool TryProjectTrackedPointToLocal(Vector2 trackedPoint, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            Camera camera = ResolveTargetCamera();
            if (camera == null || targetMeshDeformer == null)
            {
                return false;
            }

            Vector3 viewportPoint = new Vector3(
                Mathf.Clamp01(trackedPoint.x / TrackedFrameWidth),
                Mathf.Clamp01(trackedPoint.y / TrackedFrameHeight),
                0.0f);

            Ray ray = camera.ViewportPointToRay(viewportPoint);
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

        private class HandInteractionState
        {
            public InteractionMode CurrentMode;
            public bool HasProjectedPoints;
            public bool HasSmoothedPoints;
            public Vector2 SmoothedThumbLocal;
            public Vector2 SmoothedIndexLocal;
            public Vector2 SmoothedGrabLocal;
            public Vector2 ThumbLocalPoint;
            public Vector2 IndexLocalPoint;
            public Vector2 GrabLocalPoint;
            public Vector3 ThumbWorldPoint;
            public Vector3 IndexWorldPoint;
            public Vector3 GrabWorldPoint;
            public bool HasActiveBoundaryTarget;
            public int ActiveBoundaryArrayIndex = -1;
            public Vector2 ActiveBoundaryLocalPoint;
            public Vector3 ActiveBoundaryWorldPoint;
            public bool IsGrabLocked;
            public int LockedBoundaryArrayIndex = -1;
            public bool HasPreviousGrabPoint;
            public Vector2 PreviousGrabLocal;
            public float PendingGrabSeconds;
        }
    }
}
