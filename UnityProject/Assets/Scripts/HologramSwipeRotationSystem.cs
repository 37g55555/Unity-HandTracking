using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class HologramSwipeRotationSystem : MonoBehaviour
    {
        private const int LandmarksPerHand = 21;
        private const int MaxHands = 2;
        private const int IndexTipLandmark = 8;
        private const float TrackedFrameWidth = 1920.0f;

        private static readonly int[] PalmLandmarkIndices = { 0, 5, 9, 13, 17 };

        [Header("References")]
        [SerializeField] private Transform rotationTarget;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private string fallbackTargetName = "StarCharacter";

        [Header("Fingertip Swipe")]
        [SerializeField] private bool mirrorHorizontal;
        [SerializeField, Range(0, LandmarksPerHand - 1)] private int fingertipLandmarkIndex = IndexTipLandmark;
        [SerializeField, Min(1.0f)] private float swipeThresholdPixels = 180.0f;
        [SerializeField, Min(1.0f)] private float minimumSwipeSpeedPixelsPerSecond = 900.0f;
        [SerializeField, Min(1.0f)] private float dominantAxisBias = 1.25f;
        [SerializeField, Min(0.01f)] private float swipeWindowSeconds = 0.24f;
        [SerializeField, Min(0.0f)] private float swipeCooldownSeconds = 0.18f;
        [SerializeField, Min(0.0f)] private float fingertipSmoothingSpeed = 20.0f;

        [Header("Spin")]
        [SerializeField, Min(0.0f)] private float singleSwipeSpinDegrees = 720.0f;
        [SerializeField, Min(0.01f)] private float singleSwipeSpinDurationSeconds = 0.7f;
        [SerializeField, Min(0.0f)] private float maximumSpinRotations = 5.0f;
        [SerializeField, Min(0.0f)] private float minimumReturnToZeroSpeedDegreesPerSecond = 180.0f;

        [Header("Palm Fly Away")]
        [SerializeField, Min(1.0f)] private float palmSwipeThresholdPixels = 260.0f;
        [SerializeField, Min(1.0f)] private float palmMinimumSwipeSpeedPixelsPerSecond = 850.0f;
        [SerializeField, Min(0.01f)] private float palmSwipeWindowSeconds = 0.35f;
        [SerializeField, Min(0.0f)] private float palmSmoothingSpeed = 16.0f;
        [SerializeField, Min(0.0f)] private float minimumPalmSpanPixels = 80.0f;
        [SerializeField, Min(0.0f)] private float flyAwayDurationSeconds = 0.65f;
        [SerializeField] private Vector3 flyAwayLocalOffset = new Vector3(-4.0f, 3.0f, 0.0f);

        [Header("Completion")]
        [SerializeField] private string nextMainSceneName = "Mission4";

        private bool hasSmoothedFingertip;
        private Vector2 smoothedFingertip;
        private Vector2 swipeStartFingertip;
        private float swipeStartTime;
        private float cooldownUntilTime;
        private float accumulatedSpinDegrees;
        private Coroutine spinRoutine;
        private bool spinLocked;
        private bool isSpinning;
        private bool isReturningToZero;
        private bool canAcceptFlyAwayGesture;
        private bool isFlyingAway;
        private bool hasSmoothedPalm;
        private Vector2 smoothedPalmCenter;
        private Vector2 palmSwipeStartCenter;
        private float palmSwipeStartTime;
        private float returnToZeroSpeedDegreesPerSecond;
        private Coroutine returnToZeroRoutine;
        private Coroutine flyAwayRoutine;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            mediaPipeReceiver?.StartReceiver();
        }

        private void Update()
        {
            ResolveReferences();

            if (rotationTarget == null || mediaPipeReceiver == null)
            {
                ResetSwipeTracking();
                ResetPalmTracking();
                return;
            }

            if (!TryGetFingertipPoint(out Vector2 fingertipPoint))
            {
                ResetSwipeTracking();
            }
            else if (!spinLocked && !isFlyingAway && !isSpinning && !isReturningToZero)
            {
                UpdateSmoothedFingertip(fingertipPoint);
                DetectSwipe();
            }
            else
            {
                ResetSwipeTracking();
            }

            if (isFlyingAway)
            {
                ResetPalmTracking();
                return;
            }

            if (!canAcceptFlyAwayGesture || isReturningToZero)
            {
                ResetPalmTracking();
                return;
            }

            if (!TryGetPalmCenter(out Vector2 palmCenter))
            {
                ResetPalmTracking();
                return;
            }

            UpdateSmoothedPalm(palmCenter);
            DetectPalmFlyAwayGesture();
        }

        private void OnValidate()
        {
            fingertipLandmarkIndex = Mathf.Clamp(fingertipLandmarkIndex, 0, LandmarksPerHand - 1);
        }

        private void ResolveReferences()
        {
            if (rotationTarget == null)
            {
                Transform target = FindTransformRecursive(transform, fallbackTargetName);
                if (target == null)
                {
                    GameObject targetObject = GameObject.Find(fallbackTargetName);
                    target = targetObject != null ? targetObject.transform : null;
                }

                rotationTarget = target;
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

        private bool TryGetFingertipPoint(out Vector2 fingertipPoint)
        {
            fingertipPoint = Vector2.zero;
            if (!mediaPipeReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                return false;
            }

            int handCount = Mathf.Min(
                MaxHands,
                landmarks.Length / LandmarksPerHand);

            for (int handIndex = 0; handIndex < handCount; handIndex++)
            {
                int landmarkIndex = (handIndex * LandmarksPerHand) + fingertipLandmarkIndex;
                if (landmarkIndex < 0 || landmarkIndex >= landmarks.Length)
                {
                    continue;
                }

                Vector3 landmark = landmarks[landmarkIndex];
                float x = mirrorHorizontal
                    ? TrackedFrameWidth - landmark.x
                    : landmark.x;
                fingertipPoint = new Vector2(x, landmark.y);
                return true;
            }

            return false;
        }

        private bool TryGetPalmCenter(out Vector2 palmCenter)
        {
            palmCenter = Vector2.zero;
            if (!mediaPipeReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                return false;
            }

            int handCount = Mathf.Min(
                MaxHands,
                landmarks.Length / LandmarksPerHand);

            float bestSpan = 0.0f;
            Vector2 bestCenter = Vector2.zero;
            bool foundHand = false;

            for (int handIndex = 0; handIndex < handCount; handIndex++)
            {
                if (!TryGetPalmForHand(landmarks, handIndex, out Vector2 center, out float span))
                {
                    continue;
                }

                if (span > bestSpan)
                {
                    bestSpan = span;
                    bestCenter = center;
                    foundHand = true;
                }
            }

            if (!foundHand || bestSpan < minimumPalmSpanPixels)
            {
                return false;
            }

            palmCenter = bestCenter;
            return true;
        }

        private bool TryGetPalmForHand(Vector3[] landmarks, int handIndex, out Vector2 center, out float span)
        {
            center = Vector2.zero;
            span = 0.0f;

            int startIndex = handIndex * LandmarksPerHand;
            Vector2[] points = new Vector2[PalmLandmarkIndices.Length];

            for (int i = 0; i < PalmLandmarkIndices.Length; i++)
            {
                int landmarkIndex = startIndex + PalmLandmarkIndices[i];
                if (landmarkIndex < 0 || landmarkIndex >= landmarks.Length)
                {
                    return false;
                }

                Vector3 landmark = landmarks[landmarkIndex];
                float x = mirrorHorizontal
                    ? TrackedFrameWidth - landmark.x
                    : landmark.x;
                points[i] = new Vector2(x, landmark.y);
                center += points[i];
            }

            center /= PalmLandmarkIndices.Length;

            for (int i = 0; i < points.Length; i++)
            {
                for (int j = i + 1; j < points.Length; j++)
                {
                    span = Mathf.Max(span, Vector2.Distance(points[i], points[j]));
                }
            }

            return true;
        }

        private void UpdateSmoothedFingertip(Vector2 fingertipPoint)
        {
            if (!hasSmoothedFingertip)
            {
                smoothedFingertip = fingertipPoint;
                swipeStartFingertip = fingertipPoint;
                swipeStartTime = Time.unscaledTime;
                hasSmoothedFingertip = true;
                return;
            }

            float blend = fingertipSmoothingSpeed <= 0.0f
                ? 1.0f
                : 1.0f - Mathf.Exp(-fingertipSmoothingSpeed * Time.unscaledDeltaTime);
            smoothedFingertip = Vector2.Lerp(smoothedFingertip, fingertipPoint, blend);
        }

        private void DetectSwipe()
        {
            float now = Time.unscaledTime;
            if (now < cooldownUntilTime)
            {
                swipeStartFingertip = smoothedFingertip;
                swipeStartTime = now;
                return;
            }

            float elapsed = Mathf.Max(0.001f, now - swipeStartTime);
            Vector2 delta = smoothedFingertip - swipeStartFingertip;
            float distance = delta.magnitude;
            float speed = distance / elapsed;

            if (elapsed <= swipeWindowSeconds &&
                distance >= swipeThresholdPixels &&
                speed >= minimumSwipeSpeedPixelsPerSecond &&
                IsDominantAxisSwipe(delta))
            {
                AddSpinFromSwipe(delta);
                cooldownUntilTime = now + swipeCooldownSeconds;
                swipeStartFingertip = smoothedFingertip;
                swipeStartTime = now;
                return;
            }

            if (elapsed > swipeWindowSeconds)
            {
                swipeStartFingertip = smoothedFingertip;
                swipeStartTime = now;
            }
        }

        private bool IsDominantAxisSwipe(Vector2 delta)
        {
            float absX = Mathf.Abs(delta.x);
            float absY = Mathf.Abs(delta.y);

            if (absX >= absY)
            {
                return absY <= 0.001f || absX >= absY * dominantAxisBias;
            }

            return absX <= 0.001f || absY >= absX * dominantAxisBias;
        }

        private void AddSpinFromSwipe(Vector2 delta)
        {
            if (rotationTarget == null || spinLocked || isSpinning || isReturningToZero || isFlyingAway)
            {
                return;
            }

            float maxSpinDegrees = maximumSpinRotations * 360.0f;
            if (maximumSpinRotations > 0.0f && accumulatedSpinDegrees >= maxSpinDegrees - 0.01f)
            {
                spinLocked = true;
                StartReturnToZero(singleSwipeSpinDegrees / Mathf.Max(0.01f, singleSwipeSpinDurationSeconds));
                return;
            }

            float absX = Mathf.Abs(delta.x);
            float absY = Mathf.Abs(delta.y);
            Vector3 localAxis;
            float signedDirection;

            if (absX >= absY)
            {
                localAxis = Vector3.up;
                signedDirection = delta.x > 0.0f ? -1.0f : 1.0f;
            }
            else
            {
                localAxis = Vector3.right;
                signedDirection = delta.y > 0.0f ? 1.0f : -1.0f;
            }

            float spinDegrees = Mathf.Max(0.0f, singleSwipeSpinDegrees);
            if (maximumSpinRotations > 0.0f)
            {
                spinDegrees = Mathf.Min(spinDegrees, Mathf.Max(0.0f, maxSpinDegrees - accumulatedSpinDegrees));
            }

            if (spinDegrees <= 0.0f)
            {
                spinLocked = true;
                StartReturnToZero(singleSwipeSpinDegrees / Mathf.Max(0.01f, singleSwipeSpinDurationSeconds));
                return;
            }

            spinRoutine = StartCoroutine(SpinOnceRoutine(localAxis * signedDirection, spinDegrees));
        }

        private IEnumerator SpinOnceRoutine(Vector3 localAxis, float spinDegrees)
        {
            isSpinning = true;
            ResetSwipeTracking();

            Vector3 normalizedAxis = localAxis.sqrMagnitude > 0.0001f
                ? localAxis.normalized
                : Vector3.up;
            float duration = Mathf.Max(0.01f, singleSwipeSpinDurationSeconds);
            float elapsed = 0.0f;
            float previousDegrees = 0.0f;

            while (elapsed < duration && rotationTarget != null && !isFlyingAway && !isReturningToZero)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
                float currentDegrees = spinDegrees * eased;
                float frameDegrees = currentDegrees - previousDegrees;
                rotationTarget.localRotation *= Quaternion.AngleAxis(frameDegrees, normalizedAxis);
                previousDegrees = currentDegrees;
                yield return null;
            }

            if (rotationTarget != null && !isFlyingAway && !isReturningToZero && previousDegrees < spinDegrees)
            {
                rotationTarget.localRotation *= Quaternion.AngleAxis(spinDegrees - previousDegrees, normalizedAxis);
            }

            accumulatedSpinDegrees += spinDegrees;
            isSpinning = false;
            spinRoutine = null;

            float maxSpinDegrees = maximumSpinRotations * 360.0f;
            if (maximumSpinRotations > 0.0f && accumulatedSpinDegrees >= maxSpinDegrees - 0.01f)
            {
                spinLocked = true;
                StartReturnToZero(spinDegrees / duration);
            }
        }

        private void StartReturnToZero(float currentSpinSpeed)
        {
            if (rotationTarget == null || isReturningToZero)
            {
                return;
            }

            canAcceptFlyAwayGesture = false;
            isReturningToZero = true;
            returnToZeroSpeedDegreesPerSecond = Mathf.Max(
                minimumReturnToZeroSpeedDegreesPerSecond,
                currentSpinSpeed);
            ResetSwipeTracking();
            ResetPalmTracking();

            if (returnToZeroRoutine != null)
            {
                StopCoroutine(returnToZeroRoutine);
            }

            returnToZeroRoutine = StartCoroutine(ReturnToZeroRoutine());
        }

        private IEnumerator ReturnToZeroRoutine()
        {
            Quaternion endRotation = Quaternion.identity;

            while (rotationTarget != null)
            {
                float angle = Quaternion.Angle(rotationTarget.localRotation, endRotation);
                if (angle <= 0.05f)
                {
                    break;
                }

                float step = Mathf.Max(0.0f, returnToZeroSpeedDegreesPerSecond) * Time.unscaledDeltaTime;
                if (step <= 0.0f)
                {
                    step = angle;
                }

                rotationTarget.localRotation = Quaternion.RotateTowards(
                    rotationTarget.localRotation,
                    endRotation,
                    step);
                yield return null;
            }

            if (rotationTarget != null)
            {
                rotationTarget.localRotation = endRotation;
            }

            isReturningToZero = false;
            canAcceptFlyAwayGesture = true;
            returnToZeroRoutine = null;
        }

        private void UpdateSmoothedPalm(Vector2 palmCenter)
        {
            if (!hasSmoothedPalm)
            {
                smoothedPalmCenter = palmCenter;
                palmSwipeStartCenter = palmCenter;
                palmSwipeStartTime = Time.unscaledTime;
                hasSmoothedPalm = true;
                return;
            }

            float blend = palmSmoothingSpeed <= 0.0f
                ? 1.0f
                : 1.0f - Mathf.Exp(-palmSmoothingSpeed * Time.unscaledDeltaTime);
            smoothedPalmCenter = Vector2.Lerp(smoothedPalmCenter, palmCenter, blend);
        }

        private void DetectPalmFlyAwayGesture()
        {
            float now = Time.unscaledTime;
            float elapsed = Mathf.Max(0.001f, now - palmSwipeStartTime);
            Vector2 delta = smoothedPalmCenter - palmSwipeStartCenter;
            float distance = delta.magnitude;
            float speed = distance / elapsed;

            if (elapsed <= palmSwipeWindowSeconds &&
                distance >= palmSwipeThresholdPixels &&
                speed >= palmMinimumSwipeSpeedPixelsPerSecond &&
                IsUpLeftPalmSwipe(delta))
            {
                StartFlyAway();
                palmSwipeStartCenter = smoothedPalmCenter;
                palmSwipeStartTime = now;
                return;
            }

            if (elapsed > palmSwipeWindowSeconds)
            {
                palmSwipeStartCenter = smoothedPalmCenter;
                palmSwipeStartTime = now;
            }
        }

        private static bool IsUpLeftPalmSwipe(Vector2 delta)
        {
            if (delta.x >= 0.0f || delta.y <= 0.0f)
            {
                return false;
            }

            float absX = Mathf.Abs(delta.x);
            float absY = Mathf.Abs(delta.y);
            float smaller = Mathf.Min(absX, absY);
            float larger = Mathf.Max(absX, absY);
            return larger <= smaller * 2.2f;
        }

        private void StartFlyAway()
        {
            if (rotationTarget == null || isFlyingAway || isReturningToZero || !canAcceptFlyAwayGesture)
            {
                return;
            }

            isFlyingAway = true;
            spinLocked = true;
            if (spinRoutine != null)
            {
                StopCoroutine(spinRoutine);
                spinRoutine = null;
            }

            isSpinning = false;

            if (flyAwayRoutine != null)
            {
                StopCoroutine(flyAwayRoutine);
            }

            flyAwayRoutine = StartCoroutine(FlyAwayRoutine());
        }

        private IEnumerator FlyAwayRoutine()
        {
            Vector3 startPosition = rotationTarget.localPosition;
            Vector3 endPosition = startPosition + flyAwayLocalOffset;
            Vector3 startScale = rotationTarget.localScale;
            float duration = Mathf.Max(0.0f, flyAwayDurationSeconds);

            if (duration <= 0.0f)
            {
                rotationTarget.localPosition = endPosition;
                rotationTarget.localScale = Vector3.zero;
                CompleteFlyAway();
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration && rotationTarget != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                rotationTarget.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, eased);
                rotationTarget.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, eased);
                yield return null;
            }

            if (rotationTarget != null)
            {
                rotationTarget.localPosition = endPosition;
                rotationTarget.localScale = Vector3.zero;
            }

            CompleteFlyAway();
        }

        private void CompleteFlyAway()
        {
            if (rotationTarget != null)
            {
                rotationTarget.gameObject.SetActive(false);
            }

            flyAwayRoutine = null;
            FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission4);

            if (!string.IsNullOrWhiteSpace(nextMainSceneName))
            {
                SceneManager.LoadScene(nextMainSceneName, LoadSceneMode.Single);
            }
        }

        private void ResetSwipeTracking()
        {
            hasSmoothedFingertip = false;
        }

        private void ResetPalmTracking()
        {
            hasSmoothedPalm = false;
        }

        private static Transform FindTransformRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            if (root.name == targetName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransformRecursive(root.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
