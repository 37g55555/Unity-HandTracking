using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class Mission2SunHandSystem : MonoBehaviour
    {
        private const int IndexTipLandmark = 8;

        [Header("References")]
        [SerializeField] private Transform sunTransform;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform targetShadowTransform;
        [SerializeField] private Mission2StarMeshIntroAnimator mission2Controller;

        [Header("Sun Follow")]
        [SerializeField] private bool mirrorHandX;
        [SerializeField, Min(0.0f)] private float startDelaySeconds = 2.0f;
        [SerializeField, Min(0.0f)] private float sunFollowSpeed = 12.0f;
        [SerializeField, Min(0.0f)] private float initialHandAttachSeconds = 0.65f;
        [SerializeField, Min(0.0f)] private float screenEdgePaddingWorld = 0.08f;

        [Header("Shadow Scale")]
        [SerializeField, Min(0.01f)] private float nearDistanceX = 0.85f;
        [SerializeField, Min(0.01f)] private float farDistanceX = 8.0f;
        [SerializeField, Min(0.01f)] private float minShadowScale = 0.8f;
        [SerializeField, Min(0.01f)] private float maxShadowScale = 3.0f;
        [SerializeField, Min(0.0f)] private float shadowScaleSpeed = 7.0f;

        [Header("Completion")]
        [SerializeField] private string nextSceneName = "Mission3";
        [SerializeField, Min(0.0f)] private float scaleCompletionTolerance = 0.01f;
        [SerializeField, Range(0.0f, 1.0f)] private float completionDingVolume = 0.85f;
        [SerializeField, Min(0.0f)] private float sceneTransitionDelaySeconds;

        private float lockedSunY;
        private float lockedSunZ;
        private float activationTime;
        private float initialAttachStartTime;
        private float initialAttachStartX;
        private bool hasInitialAttachStart;
        private bool isInitialAttachActive;
        private bool missionCompleted;
        private AudioClip completionDingClip;

        private void Awake()
        {
            if (sunTransform != null)
            {
                lockedSunY = sunTransform.position.y;
                lockedSunZ = sunTransform.position.z;
            }

            activationTime = Time.unscaledTime + startDelaySeconds;
        }

        private void OnValidate()
        {
            farDistanceX = Mathf.Max(farDistanceX, nearDistanceX + 0.01f);
            minShadowScale = Mathf.Min(minShadowScale, maxShadowScale);
        }

        public void BeginInteraction()
        {
            if (sunTransform != null)
            {
                lockedSunY = sunTransform.position.y;
                lockedSunZ = sunTransform.position.z;
            }

            activationTime = Time.unscaledTime + startDelaySeconds;
            hasInitialAttachStart = false;
            isInitialAttachActive = false;
            missionCompleted = false;
            enabled = true;
        }

        public void DebugCompleteMission()
        {
            CompleteMission();
        }

        private void LateUpdate()
        {
            if (missionCompleted)
            {
                return;
            }

            if (Time.unscaledTime < activationTime)
            {
                return;
            }

            UpdateSunXFromHand();
            UpdateShadowScaleFromSunDistance();
        }

        private void UpdateSunXFromHand()
        {
            if (sunTransform == null || !TryGetHandViewportX(out float handViewportX))
            {
                return;
            }

            if (!TryGetWorldXAtSunPlane(handViewportX, out float handWorldX))
            {
                return;
            }

            float clampedX = ClampSunXToCamera(handWorldX);
            Vector3 currentPosition = sunTransform.position;
            float nextX = GetNextSunX(currentPosition.x, clampedX);
            sunTransform.position = new Vector3(nextX, lockedSunY, lockedSunZ);
        }

        private float GetNextSunX(float currentX, float targetX)
        {
            if (!hasInitialAttachStart)
            {
                hasInitialAttachStart = true;
                isInitialAttachActive = initialHandAttachSeconds > 0.0f;
                initialAttachStartTime = Time.unscaledTime;
                initialAttachStartX = currentX;
            }

            if (isInitialAttachActive)
            {
                float duration = Mathf.Max(0.0f, initialHandAttachSeconds);
                if (duration <= 0.0f)
                {
                    isInitialAttachActive = false;
                    return targetX;
                }

                float elapsed = Time.unscaledTime - initialAttachStartTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                if (t >= 1.0f)
                {
                    isInitialAttachActive = false;
                }

                return Mathf.LerpUnclamped(initialAttachStartX, targetX, eased);
            }

            float blend = GetFrameBlend(sunFollowSpeed);
            return Mathf.Lerp(currentX, targetX, blend);
        }

        private void UpdateShadowScaleFromSunDistance()
        {
            if (sunTransform == null || targetShadowTransform == null)
            {
                return;
            }

            float shadowX = GetShadowCenterX();
            float distanceX = Mathf.Abs(sunTransform.position.x - shadowX);
            float closeness = Mathf.InverseLerp(farDistanceX, nearDistanceX, distanceX);
            float easedCloseness = Mathf.SmoothStep(0.0f, 1.0f, closeness);
            float targetScale = Mathf.Lerp(minShadowScale, maxShadowScale, easedCloseness);
            float currentScale = targetShadowTransform.localScale.x;
            float nextScale = Mathf.Lerp(currentScale, targetScale, GetFrameBlend(shadowScaleSpeed));

            ApplyScaleWithBottomAnchor(targetShadowTransform, nextScale);

            if (targetShadowTransform.localScale.x >= maxShadowScale - scaleCompletionTolerance)
            {
                CompleteMission();
            }
        }

        private bool TryGetHandViewportX(out float viewportX)
        {
            viewportX = 0.5f;
            if (mediaPipeReceiver == null || !mediaPipeReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                return false;
            }

            int handCount = Mathf.Min(
                MediaPipeMeshDeformationInput.MaxHands,
                landmarks.Length / MediaPipeMeshDeformationInput.LandmarksPerHand);

            for (int handIndex = 0; handIndex < handCount; handIndex++)
            {
                int landmarkIndex = (handIndex * MediaPipeMeshDeformationInput.LandmarksPerHand) + IndexTipLandmark;
                if (landmarkIndex < 0 || landmarkIndex >= landmarks.Length)
                {
                    continue;
                }

                float normalizedX = Mathf.Clamp01(landmarks[landmarkIndex].x / MediaPipeMeshDeformationInput.TrackedFrameWidth);
                viewportX = mirrorHandX ? 1.0f - normalizedX : normalizedX;
                return true;
            }

            return false;
        }

        private bool TryGetWorldXAtSunPlane(float viewportX, out float worldX)
        {
            worldX = 0.0f;
            if (targetCamera == null || sunTransform == null)
            {
                return false;
            }

            float planeDistance = Mathf.Abs(Vector3.Dot(
                sunTransform.position - targetCamera.transform.position,
                targetCamera.transform.forward));
            planeDistance = Mathf.Max(targetCamera.nearClipPlane, planeDistance);
            Vector3 worldPoint = targetCamera.ViewportToWorldPoint(new Vector3(viewportX, 0.5f, planeDistance));
            worldX = worldPoint.x;
            return true;
        }

        private float ClampSunXToCamera(float worldX)
        {
            if (!TryGetCameraXBoundsAtSunPlane(out float minX, out float maxX))
            {
                return worldX;
            }

            float margin = GetSunHalfWidthWorld() + screenEdgePaddingWorld;
            float clampedMin = minX + margin;
            float clampedMax = maxX - margin;
            if (clampedMin > clampedMax)
            {
                return (minX + maxX) * 0.5f;
            }

            return Mathf.Clamp(worldX, clampedMin, clampedMax);
        }

        private bool TryGetCameraXBoundsAtSunPlane(out float minX, out float maxX)
        {
            minX = 0.0f;
            maxX = 0.0f;

            if (targetCamera == null || sunTransform == null)
            {
                return false;
            }

            float planeDistance = Mathf.Abs(Vector3.Dot(
                sunTransform.position - targetCamera.transform.position,
                targetCamera.transform.forward));
            planeDistance = Mathf.Max(targetCamera.nearClipPlane, planeDistance);
            Vector3 left = targetCamera.ViewportToWorldPoint(new Vector3(0.0f, 0.5f, planeDistance));
            Vector3 right = targetCamera.ViewportToWorldPoint(new Vector3(1.0f, 0.5f, planeDistance));
            minX = Mathf.Min(left.x, right.x);
            maxX = Mathf.Max(left.x, right.x);
            return true;
        }

        private float GetSunHalfWidthWorld()
        {
            Renderer sunRenderer = sunTransform != null ? sunTransform.GetComponentInChildren<Renderer>() : null;
            if (sunRenderer != null && sunRenderer.bounds.size.x > 0.0f)
            {
                return sunRenderer.bounds.extents.x;
            }

            return 0.9f;
        }

        private float GetShadowCenterX()
        {
            Renderer renderer = targetShadowTransform != null ? targetShadowTransform.GetComponentInChildren<Renderer>() : null;
            if (renderer != null && renderer.bounds.size.sqrMagnitude > 0.0001f)
            {
                return renderer.bounds.center.x;
            }

            return targetShadowTransform != null ? targetShadowTransform.position.x : 0.0f;
        }

        private void ApplyScaleWithBottomAnchor(Transform rootTransform, float uniformScale)
        {
            if (rootTransform == null)
            {
                return;
            }

            Vector3 bottomAnchorBefore = GetShadowBottomAnchor(rootTransform);
            rootTransform.localScale = Vector3.one * uniformScale;
            Vector3 bottomAnchorAfter = GetShadowBottomAnchor(rootTransform);
            Vector3 correction = bottomAnchorBefore - bottomAnchorAfter;
            correction.z = 0.0f;
            rootTransform.position += correction;
        }

        private Vector3 GetShadowBottomAnchor(Transform rootTransform)
        {
            Renderer renderer = rootTransform.GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.bounds.size.sqrMagnitude > 0.0001f)
            {
                Bounds bounds = renderer.bounds;
                return new Vector3(bounds.center.x, bounds.min.y, rootTransform.position.z);
            }

            return rootTransform.position;
        }

        private void CompleteMission()
        {
            if (missionCompleted)
            {
                return;
            }

            missionCompleted = true;
            StartCoroutine(CompleteMissionRoutine());
        }

        private IEnumerator CompleteMissionRoutine()
        {
            PlayCompletionDing();

            Mission2StarMeshIntroAnimator resolvedController = ResolveMission2Controller();
            if (resolvedController != null)
            {
                resolvedController.HideInteractionInstruction();
            }

            StopMediaPipeTracking();

            if (resolvedController != null)
            {
                yield return resolvedController.EnterOutroAndWaitRoutine();
            }

            if (sceneTransitionDelaySeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(sceneTransitionDelaySeconds);
            }

            FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission3);
            LoadNextScene();
        }

        private void LoadNextScene()
        {
            if (string.IsNullOrWhiteSpace(nextSceneName))
            {
                return;
            }

            SceneFlowController sceneFlowController = FindObjectOfType<SceneFlowController>();
            if (sceneFlowController != null)
            {
                sceneFlowController.LoadScene(nextSceneName);
                return;
            }

            SceneManager.LoadScene(nextSceneName);
        }

        private Mission2StarMeshIntroAnimator ResolveMission2Controller()
        {
            return mission2Controller;
        }

        private static void StopMediaPipeTracking()
        {
            MediaPipeInteractionVisualizer[] visualizers = FindObjectsOfType<MediaPipeInteractionVisualizer>();
            for (int i = 0; i < visualizers.Length; i++)
            {
                visualizers[i].HideRuntimeVisuals();
                visualizers[i].enabled = false;
            }

            MediaPipeMeshDeformationInput[] deformationInputs = FindObjectsOfType<MediaPipeMeshDeformationInput>();
            for (int i = 0; i < deformationInputs.Length; i++)
            {
                deformationInputs[i].enabled = false;
            }

            MediaPipeUdpReceiver[] receivers = FindObjectsOfType<MediaPipeUdpReceiver>();
            for (int i = 0; i < receivers.Length; i++)
            {
                receivers[i].StopReceiver();
                receivers[i].enabled = false;
            }

            MediaPipeTrackingProcessLauncher[] launchers = FindObjectsOfType<MediaPipeTrackingProcessLauncher>();
            for (int i = 0; i < launchers.Length; i++)
            {
                launchers[i].enabled = false;
            }
        }

        private void PlayCompletionDing()
        {
            if (completionDingVolume <= 0.0f)
            {
                return;
            }

            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.0f;

            if (completionDingClip == null)
            {
                completionDingClip = CreateCompletionDingClip();
            }

            audioSource.PlayOneShot(completionDingClip, completionDingVolume);
        }

        private static AudioClip CreateCompletionDingClip()
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

            AudioClip clip = AudioClip.Create("Mission2CompletionDing", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float GetFrameBlend(float speed)
        {
            if (speed <= 0.0f)
            {
                return 1.0f;
            }

            return 1.0f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (completionDingClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(completionDingClip);
            }
            else
            {
                DestroyImmediate(completionDingClip);
            }
        }
    }
}
