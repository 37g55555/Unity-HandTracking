using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public sealed class HologramSwipeRotationSystem : MonoBehaviour
    {
        private const int LandmarksPerHand = 21;
        private const int MaxHands = 2;
        private const int IndexTipLandmark = 8;
        private const float TrackedFrameWidth = 1920.0f;

        private static readonly int[] PalmLandmarkIndices = { 0, 5, 9, 13, 17 };
        private static readonly Vector2 FallbackSpinProgressGaugeSize = new Vector2(300.0f, 18.0f);

        [Header("References")]
        [SerializeField] private Transform rotationTarget;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private HologramVideoPanelPlayer postSpinVideoPlayer;

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

        [Header("Spin Progress Gauge")]
        [SerializeField] private bool showSpinProgressGauge = true;
        [SerializeField] private RectTransform spinProgressGaugeRoot;
        [SerializeField] private RectTransform spinProgressGaugeFill;
        [SerializeField, Min(0.0f)] private float spinProgressGaugePadding = 3.0f;
        [SerializeField, Min(1)] private int spinProgressGaugeTickCount = 5;
        [SerializeField] private Color spinProgressTrackColor = new Color(0.04f, 0.04f, 0.04f, 0.72f);
        [SerializeField] private Color spinProgressFrameColor = new Color(1.0f, 1.0f, 1.0f, 0.85f);
        [SerializeField] private Color spinProgressFillColor = new Color(1.0f, 0.78f, 0.16f, 1.0f);
        [SerializeField] private Color spinProgressGlowColor = new Color(1.0f, 0.74f, 0.18f, 0.24f);
        [SerializeField] private Color spinProgressTickColor = new Color(1.0f, 1.0f, 1.0f, 0.62f);

        [Header("Rotation Feedback")]
        [SerializeField] private AudioSource rotationAudioSource;
        [SerializeField, Range(0.0f, 1.0f)] private float rotationDingVolume = 0.85f;

        [Header("Palm Fly Away")]
        [SerializeField] private bool playPostSpinVideoBeforeFlyAway = true;
        [SerializeField] private string postSpinVideoRelativePath = "HologramVideos/starChar_1_2_tts.mp4";
        [SerializeField, Min(0.0f)] private float postSpinVideoDelaySeconds = 1.0f;
        [SerializeField] private bool replaceTargetBeforePostSpinVideo = true;
        [SerializeField] private string postSpinReplacementResourcePath = "Models/Weeping_Star";
        [SerializeField] private bool destroyPreviousTargetAfterReplacement = true;
        [SerializeField, Min(1.0f)] private float palmSwipeThresholdPixels = 260.0f;
        [SerializeField, Min(1.0f)] private float palmMinimumSwipeSpeedPixelsPerSecond = 850.0f;
        [SerializeField, Min(0.01f)] private float palmSwipeWindowSeconds = 0.35f;
        [SerializeField, Min(0.0f)] private float palmSmoothingSpeed = 16.0f;
        [SerializeField, Min(0.0f)] private float minimumPalmSpanPixels = 80.0f;
        [SerializeField, Min(0.0f)] private float flyAwayDurationSeconds = 0.65f;
        [SerializeField] private Vector3 flyAwayLocalOffset = new Vector3(-4.0f, 3.0f, 0.0f);

        [Header("Fly Away Instruction")]
        [SerializeField] private GameObject flyAwayInstructionObject;
        [SerializeField] private Text flyAwayInstructionText;
        [SerializeField] private string flyAwayInstructionMessage = "스와이프하여 벽으로 돌려보내기";

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
        private bool postSpinVideoPlayed;
        private bool waitingForPostSpinVideo;
        private bool postSpinTargetReplaced;
        private AudioClip rotationDingClip;
        private RectTransform spinProgressGaugeGlow;
        private RectTransform spinProgressGaugeTrack;
        private RectTransform spinProgressGaugeShine;
        private RectTransform[] spinProgressGaugeTicks;
        private Image spinProgressGaugeGlowImage;
        private Image spinProgressGaugeTrackImage;
        private Image spinProgressGaugeFillImage;
        private Image spinProgressGaugeShineImage;
        private Outline spinProgressGaugeTrackOutline;

        private void Awake()
        {
            ResolveReferences();
            SetFlyAwayInstructionVisible(false);
            SetSpinProgressGaugeVisible(false);
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (waitingForPostSpinVideo)
            {
                waitingForPostSpinVideo = false;
                canAcceptFlyAwayGesture = true;
                ResetSwipeTracking();
                ResetPalmTracking();
                SetFlyAwayInstructionVisible(true);
                SetSpinProgressGaugeVisible(false);
            }
            else
            {
                SetFlyAwayInstructionVisible(false);
                SetSpinProgressGaugeVisible(true);
            }
        }

        private void OnDisable()
        {
            SetSpinProgressGaugeVisible(false);
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
            spinProgressGaugeTickCount = Mathf.Max(1, spinProgressGaugeTickCount);
        }

        private void ResolveReferences()
        {
            if (rotationAudioSource == null)
            {
                rotationAudioSource = GetComponent<AudioSource>();
            }

            ResolveFlyAwayInstruction();
        }

        private void ResolveFlyAwayInstruction()
        {
            if (flyAwayInstructionText == null && flyAwayInstructionObject != null)
            {
                flyAwayInstructionText = flyAwayInstructionObject.GetComponent<Text>();
            }

            if (flyAwayInstructionObject == null && flyAwayInstructionText != null)
            {
                flyAwayInstructionObject = flyAwayInstructionText.gameObject;
            }
        }

        private void SetFlyAwayInstructionVisible(bool isVisible)
        {
            ResolveFlyAwayInstruction();

            if (flyAwayInstructionText != null)
            {
                flyAwayInstructionText.text = flyAwayInstructionMessage;
            }

            if (flyAwayInstructionObject != null)
            {
                flyAwayInstructionObject.SetActive(isVisible);
            }
            else if (flyAwayInstructionText != null)
            {
                flyAwayInstructionText.gameObject.SetActive(isVisible);
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
                SetSpinProgressGaugeValue(accumulatedSpinDegrees + currentDegrees);
                yield return null;
            }

            if (rotationTarget != null && !isFlyingAway && !isReturningToZero && previousDegrees < spinDegrees)
            {
                rotationTarget.localRotation *= Quaternion.AngleAxis(spinDegrees - previousDegrees, normalizedAxis);
            }

            accumulatedSpinDegrees += spinDegrees;
            SetSpinProgressGaugeValue(accumulatedSpinDegrees);
            PlayRotationDing();
            isSpinning = false;
            spinRoutine = null;

            float maxSpinDegrees = maximumSpinRotations * 360.0f;
            if (maximumSpinRotations > 0.0f && accumulatedSpinDegrees >= maxSpinDegrees - 0.01f)
            {
                spinLocked = true;
                StartReturnToZero(spinDegrees / duration);
            }
        }

        private void PlayRotationDing()
        {
            if (rotationDingVolume <= 0.0f)
            {
                return;
            }

            rotationAudioSource = HologramAudioPlaybackUtility.Resolve2DAudioSource(
                this,
                rotationAudioSource);

            if (rotationDingClip == null)
            {
                rotationDingClip = CreateRotationDingClip();
            }

            if (rotationAudioSource != null)
            {
                rotationAudioSource.PlayOneShot(rotationDingClip, rotationDingVolume);
            }
        }

        private static AudioClip CreateRotationDingClip()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.58f;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float attack = Mathf.Clamp01(time / 0.018f);
                float decay = Mathf.Exp(-5.2f * time);
                float primary = Mathf.Sin(2.0f * Mathf.PI * 880.0f * time);
                float overtone = 0.42f * Mathf.Sin(2.0f * Mathf.PI * 1320.0f * time);
                float chimeDelay = Mathf.Max(0.0f, time - 0.12f);
                float chime = time >= 0.12f
                    ? 0.5f * Mathf.Exp(-7.0f * chimeDelay) * Mathf.Sin(2.0f * Mathf.PI * 1760.0f * chimeDelay)
                    : 0.0f;
                samples[i] = 0.32f * attack * ((decay * (primary + overtone)) + chime);
            }

            AudioClip clip = AudioClip.Create("Mission3RotationDing", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
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
            returnToZeroRoutine = null;

            if (postSpinVideoDelaySeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(postSpinVideoDelaySeconds);
            }

            if (TryPlayPostSpinVideo())
            {
                yield break;
            }

            canAcceptFlyAwayGesture = true;
        }

        private bool TryPlayPostSpinVideo()
        {
            if (!playPostSpinVideoBeforeFlyAway || postSpinVideoPlayed)
            {
                return false;
            }

            ResolveReferences();
            if (postSpinVideoPlayer == null)
            {
                return false;
            }

            postSpinVideoPlayed = true;
            waitingForPostSpinVideo = true;
            canAcceptFlyAwayGesture = false;
            ResetSwipeTracking();
            ResetPalmTracking();
            SetSpinProgressGaugeVisible(false);
            ReplaceTargetBeforePostSpinVideo();
            postSpinVideoPlayer.Play(postSpinVideoRelativePath);
            return true;
        }

        private void ReplaceTargetBeforePostSpinVideo()
        {
            if (!replaceTargetBeforePostSpinVideo ||
                postSpinTargetReplaced ||
                string.IsNullOrWhiteSpace(postSpinReplacementResourcePath))
            {
                return;
            }

            GameObject replacementPrefab = Resources.Load<GameObject>(postSpinReplacementResourcePath);
            if (replacementPrefab == null)
            {
                Debug.LogWarning($"HologramSwipeRotationSystem: replacement prefab was not found in Resources: {postSpinReplacementResourcePath}");
                return;
            }

            Transform previousTarget = rotationTarget;
            Transform parent = previousTarget != null ? previousTarget.parent : transform;
            Vector3 localPosition = previousTarget != null ? previousTarget.localPosition : Vector3.zero;
            Quaternion localRotation = previousTarget != null ? previousTarget.localRotation : Quaternion.identity;
            Vector3 localScale = previousTarget != null ? previousTarget.localScale : Vector3.one;
            string targetName = previousTarget != null && !string.IsNullOrWhiteSpace(previousTarget.name)
                ? previousTarget.name
                : "StarCharacter";

            GameObject replacement = Instantiate(replacementPrefab, parent);
            replacement.name = string.IsNullOrWhiteSpace(targetName) ? "StarCharacter" : targetName;
            replacement.SetActive(true);

            Transform replacementTransform = replacement.transform;
            replacementTransform.localPosition = localPosition;
            replacementTransform.localRotation = localRotation;
            replacementTransform.localScale = localScale;
            rotationTarget = replacementTransform;
            postSpinTargetReplaced = true;

            if (previousTarget == null || previousTarget == replacementTransform)
            {
                return;
            }

            if (destroyPreviousTargetAfterReplacement)
            {
                Destroy(previousTarget.gameObject);
            }
            else
            {
                previousTarget.gameObject.SetActive(false);
            }
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
            SetFlyAwayInstructionVisible(false);
            SetSpinProgressGaugeVisible(false);
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

        public void DebugAdvanceToNextScene()
        {
            if (flyAwayRoutine != null)
            {
                StopCoroutine(flyAwayRoutine);
                flyAwayRoutine = null;
            }

            SetSpinProgressGaugeVisible(false);
            CompleteFlyAway();
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

        private Transform FindTransformRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            if (string.Equals(root.name, targetName, System.StringComparison.Ordinal))
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

        private void EnsureSpinProgressGauge()
        {
            if (!showSpinProgressGauge)
            {
                return;
            }

            if (spinProgressGaugeRoot == null)
            {
                Transform rootTransform = FindTransformRecursive(transform, "SpinProgressGauge");
                spinProgressGaugeRoot = rootTransform as RectTransform;
            }

            if (spinProgressGaugeRoot == null)
            {
                return;
            }

            if (spinProgressGaugeGlow == null && TryFindGaugeChild("Glow", out RectTransform glow))
            {
                spinProgressGaugeGlow = glow;
            }

            if (spinProgressGaugeTrack == null && TryFindGaugeChild("Track", out RectTransform track))
            {
                spinProgressGaugeTrack = track;
            }

            if (spinProgressGaugeTrackImage == null && spinProgressGaugeTrack != null)
            {
                spinProgressGaugeTrackImage = spinProgressGaugeTrack.GetComponent<Image>();
            }

            if (spinProgressGaugeTrackOutline == null && spinProgressGaugeTrack != null)
            {
                spinProgressGaugeTrackOutline = spinProgressGaugeTrack.GetComponent<Outline>();
            }

            if (spinProgressGaugeFill == null && TryFindGaugeChild("Fill", out RectTransform fill))
            {
                spinProgressGaugeFill = fill;
            }

            if (spinProgressGaugeFillImage == null && spinProgressGaugeFill != null)
            {
                spinProgressGaugeFillImage = spinProgressGaugeFill.GetComponent<Image>();
            }

            if (spinProgressGaugeShine == null && TryFindGaugeChild("Shine", out RectTransform shine))
            {
                spinProgressGaugeShine = shine;
            }

            if (spinProgressGaugeShineImage == null && spinProgressGaugeShine != null)
            {
                spinProgressGaugeShineImage = spinProgressGaugeShine.GetComponent<Image>();
            }

            EnsureSpinProgressGaugeTicks();
            ApplySpinProgressGaugeLayout();
        }

        private void ApplySpinProgressGaugeLayout()
        {
            Vector2 gaugeSize = GetSpinProgressGaugeSize();
            float width = Mathf.Max(0.001f, gaugeSize.x);
            float height = Mathf.Max(0.001f, gaugeSize.y);
            float padding = Mathf.Min(spinProgressGaugePadding, height * 0.45f, width * 0.45f);
            float innerWidth = Mathf.Max(0.001f, width - (padding * 2.0f));
            float innerHeight = Mathf.Max(0.001f, height - (padding * 2.0f));

            LayoutCenteredGaugePart(spinProgressGaugeGlow, new Vector2(width + 34.0f, height + 18.0f), Vector2.zero);
            LayoutCenteredGaugePart(spinProgressGaugeTrack, new Vector2(width, height), Vector2.zero);

            if (spinProgressGaugeGlowImage != null)
            {
                spinProgressGaugeGlowImage.color = spinProgressGlowColor;
                spinProgressGaugeGlowImage.raycastTarget = false;
            }

            if (spinProgressGaugeTrackImage != null)
            {
                spinProgressGaugeTrackImage.color = spinProgressTrackColor;
                spinProgressGaugeTrackImage.raycastTarget = false;
            }

            if (spinProgressGaugeTrackOutline != null)
            {
                spinProgressGaugeTrackOutline.effectColor = spinProgressFrameColor;
                spinProgressGaugeTrackOutline.effectDistance = new Vector2(1.2f, 1.2f);
                spinProgressGaugeTrackOutline.useGraphicAlpha = false;
            }

            if (spinProgressGaugeFill != null)
            {
                spinProgressGaugeFill.anchorMin = new Vector2(0.5f, 0.5f);
                spinProgressGaugeFill.anchorMax = new Vector2(0.5f, 0.5f);
                spinProgressGaugeFill.pivot = new Vector2(0.0f, 0.5f);
                spinProgressGaugeFill.anchoredPosition = new Vector2(-innerWidth * 0.5f, 0.0f);

                if (spinProgressGaugeFillImage != null)
                {
                    spinProgressGaugeFillImage.color = spinProgressFillColor;
                    spinProgressGaugeFillImage.raycastTarget = false;
                }
            }

            if (spinProgressGaugeShine != null)
            {
                spinProgressGaugeShine.anchorMin = new Vector2(0.5f, 0.5f);
                spinProgressGaugeShine.anchorMax = new Vector2(0.5f, 0.5f);
                spinProgressGaugeShine.pivot = new Vector2(0.0f, 0.5f);
                spinProgressGaugeShine.anchoredPosition = new Vector2(-innerWidth * 0.5f, innerHeight * 0.24f);

                if (spinProgressGaugeShineImage != null)
                {
                    Color shineColor = Color.white;
                    shineColor.a = 0.28f;
                    spinProgressGaugeShineImage.color = shineColor;
                    spinProgressGaugeShineImage.raycastTarget = false;
                }
            }

            LayoutSpinProgressGaugeTicks(innerWidth, innerHeight);
        }

        private void SetSpinProgressGaugeVisible(bool isVisible)
        {
            if (!showSpinProgressGauge)
            {
                if (spinProgressGaugeRoot != null)
                {
                    spinProgressGaugeRoot.gameObject.SetActive(false);
                }

                return;
            }

            EnsureSpinProgressGauge();
            if (spinProgressGaugeRoot == null)
            {
                return;
            }

            spinProgressGaugeRoot.gameObject.SetActive(isVisible);
            if (isVisible)
            {
                SetSpinProgressGaugeValue(accumulatedSpinDegrees);
            }
        }

        private void SetSpinProgressGaugeValue(float spinDegrees)
        {
            if (!showSpinProgressGauge || spinProgressGaugeFill == null)
            {
                return;
            }

            float progress = GetSpinProgress01(spinDegrees);
            Vector2 gaugeSize = GetSpinProgressGaugeSize();
            float width = Mathf.Max(0.001f, gaugeSize.x);
            float height = Mathf.Max(0.001f, gaugeSize.y);
            float padding = Mathf.Min(spinProgressGaugePadding, height * 0.45f, width * 0.45f);
            float innerWidth = Mathf.Max(0.001f, width - (padding * 2.0f));
            float innerHeight = Mathf.Max(0.001f, height - (padding * 2.0f));
            float fillWidth = innerWidth * progress;

            spinProgressGaugeFill.sizeDelta = new Vector2(fillWidth, innerHeight);

            if (spinProgressGaugeShine != null)
            {
                spinProgressGaugeShine.sizeDelta = new Vector2(fillWidth, innerHeight * 0.32f);
            }
        }

        private float GetSpinProgress01(float spinDegrees)
        {
            if (maximumSpinRotations <= 0.0f)
            {
                return 0.0f;
            }

            float maxSpinDegrees = maximumSpinRotations * 360.0f;
            if (maxSpinDegrees <= 0.0f)
            {
                return 0.0f;
            }

            return Mathf.Clamp01(spinDegrees / maxSpinDegrees);
        }

        private Vector2 GetSpinProgressGaugeSize()
        {
            if (spinProgressGaugeRoot != null)
            {
                Vector2 rectSize = spinProgressGaugeRoot.rect.size;
                if (rectSize.x > 0.001f && rectSize.y > 0.001f)
                {
                    return rectSize;
                }

                Vector2 sizeDelta = spinProgressGaugeRoot.sizeDelta;
                if (sizeDelta.x > 0.001f && sizeDelta.y > 0.001f)
                {
                    return sizeDelta;
                }
            }

            return FallbackSpinProgressGaugeSize;
        }

        private void EnsureSpinProgressGaugeTicks()
        {
            int tickCount = Mathf.Max(1, spinProgressGaugeTickCount);
            int dividerCount = Mathf.Max(0, tickCount - 1);
            if (spinProgressGaugeTicks != null && spinProgressGaugeTicks.Length == dividerCount)
            {
                return;
            }

            spinProgressGaugeTicks = new RectTransform[dividerCount];
            for (int i = 0; i < dividerCount; i++)
            {
                if (TryFindGaugeChild($"Tick_{i + 1}", out RectTransform tick))
                {
                    spinProgressGaugeTicks[i] = tick;
                }
            }
        }

        private void LayoutSpinProgressGaugeTicks(float innerWidth, float innerHeight)
        {
            if (spinProgressGaugeTicks == null || spinProgressGaugeTicks.Length == 0)
            {
                return;
            }

            int tickCount = spinProgressGaugeTicks.Length + 1;
            for (int i = 0; i < spinProgressGaugeTicks.Length; i++)
            {
                RectTransform tick = spinProgressGaugeTicks[i];
                if (tick == null)
                {
                    continue;
                }

                float x = (-innerWidth * 0.5f) + (innerWidth * (i + 1) / tickCount);
                LayoutCenteredGaugePart(tick, new Vector2(2.0f, innerHeight), new Vector2(x, 0.0f));

                Image tickImage = tick.GetComponent<Image>();
                if (tickImage != null)
                {
                    tickImage.color = spinProgressTickColor;
                    tickImage.raycastTarget = false;
                }
            }
        }

        private bool TryFindGaugeChild(string childName, out RectTransform rectTransform)
        {
            rectTransform = null;
            if (spinProgressGaugeRoot == null || string.IsNullOrWhiteSpace(childName))
            {
                return false;
            }

            Transform child = spinProgressGaugeRoot.Find(childName);
            rectTransform = child as RectTransform;
            return rectTransform != null;
        }

        private static void LayoutCenteredGaugePart(RectTransform rectTransform, Vector2 size, Vector2 anchoredPosition)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        private void OnDestroy()
        {
            if (rotationDingClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(rotationDingClip);
            }
            else
            {
                DestroyImmediate(rotationDingClip);
            }
        }
    }
}
