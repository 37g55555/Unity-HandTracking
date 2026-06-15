using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class Mission3HologramDisplayLoader : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public enum Mission3Phase
        {
            Intro,
            Interaction
        }

        [Header("Phase")]
        [SerializeField] private Mission3Phase initialPhase = Mission3Phase.Intro;
        [SerializeField] private bool autoEnterInteractionAfterIntro = true;
        [SerializeField] private bool playOnStart = true;

        [Header("Intro")]
        [SerializeField] private Renderer darkRenderer;
        [SerializeField, Range(0, 255)] private int introStartAlpha = 255;
        [SerializeField, Min(0.0f)] private float introFadeInSeconds = 2.0f;
        [SerializeField] private Transform shadowStarTransform;
        [SerializeField] private float introShadowStarTargetX = -0.1f;
        [SerializeField, Min(0.0f)] private float introShadowStarMoveSeconds = 3.0f;
        [SerializeField, Min(0.0f)] private float introShadowStarRoadMoveSeconds = 2.0f;
        [SerializeField] private Vector2[] introShadowStarRoadPathPoints =
        {
            new Vector2(0.25f, -1.15f),
            new Vector2(1.25f, -1.55f),
            new Vector2(2.65f, -2.35f),
            new Vector2(4.3f, -3.25f),
            new Vector2(5.35f, -4.0f)
        };
        [SerializeField, Min(0.0f)] private float introFadeOutSeconds = 2.0f;
        [SerializeField, Min(0.0f)] private float postFadeNarrationDelaySeconds = 1.0f;
        [SerializeField, Min(0.0f)] private float shadowStarOffscreenPaddingWorld = 1.0f;
        [SerializeField, Min(0.0f)] private float shadowStarOffscreenFallbackDistance = 8.0f;
        [SerializeField] private Transform backgroundTransform;
        [SerializeField] private float introBackgroundScrollTargetX = -8.888889f;
        [SerializeField] private NarrationSubtitleSequencePlayer introNarrationPlayer;
        [SerializeField, Min(0)] private int introNarrationStartStepIndex;
        [SerializeField, Min(0)] private int introNarrationStepCount = 1;
        [SerializeField] private Camera targetCamera;

        [Header("Hologram")]
        [SerializeField] private string hologramSceneName = "Mission3_H";

        private Mission3Phase currentPhase;
        private Coroutine phaseRoutine;
        private Coroutine loadRoutine;
        private MaterialPropertyBlock darkPropertyBlock;

        public Mission3Phase CurrentPhase => currentPhase;

        private void Awake()
        {
            currentPhase = initialPhase;
            ResolveDarkRenderer();
            ResolveShadowStarTransform();
            ResolveBackgroundTransform();
            ResolveIntroNarrationPlayer();
            ResolveTargetCamera();
            SetDarkAlpha(currentPhase == Mission3Phase.Intro ? IntroStartAlpha01 : 0.0f);
        }

        private void Start()
        {
            FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission3);

            if (initialPhase == Mission3Phase.Interaction)
            {
                EnterInteraction();
                return;
            }

            EnterIntro();
            if (playOnStart)
            {
                PlayIntro();
            }
        }

        public void EnterIntro()
        {
            currentPhase = Mission3Phase.Intro;
            ResolveDarkRenderer();
            ResolveShadowStarTransform();
            ResolveBackgroundTransform();
            ResolveIntroNarrationPlayer();
            ResolveTargetCamera();
            SetDarkAlpha(IntroStartAlpha01);
        }

        public void PlayIntro()
        {
            if (phaseRoutine != null)
            {
                StopCoroutine(phaseRoutine);
            }

            EnterIntro();
            phaseRoutine = StartCoroutine(PlayIntroRoutine());
        }

        public void EnterInteraction()
        {
            currentPhase = Mission3Phase.Interaction;
            LoadHologramScene();
        }

        public void LoadHologramScene()
        {
            if (loadRoutine != null)
            {
                StopCoroutine(loadRoutine);
            }

            loadRoutine = StartCoroutine(LoadHologramSceneRoutine());
        }

        private IEnumerator PlayIntroRoutine()
        {
            yield return FadeIntroDarkAndMoveShadowStarRoutine();
            yield return PlayIntroNarrationAndScrollBackgroundRoutine();
            yield return PlayNextIntroNarrationRoutine();
            yield return MoveShadowStarAlongRoadRoutine();
            yield return FadeOutAndMoveShadowStarOffscreenRoutine();
            yield return PlayPostFadeNarrationRoutine();

            if (autoEnterInteractionAfterIntro)
            {
                EnterInteraction();
            }

            phaseRoutine = null;
        }

        private IEnumerator FadeIntroDarkAndMoveShadowStarRoutine()
        {
            float fadeDuration = Mathf.Max(0.0f, introFadeInSeconds);
            float moveDuration = Mathf.Max(0.0f, introShadowStarMoveSeconds);
            float duration = Mathf.Max(fadeDuration, moveDuration);
            Transform resolvedShadowStar = ResolveShadowStarTransform();
            Vector3 shadowStarStartPosition = resolvedShadowStar != null ? resolvedShadowStar.position : Vector3.zero;
            Vector3 shadowStarTargetPosition = shadowStarStartPosition;
            shadowStarTargetPosition.x = introShadowStarTargetX;
            SetDarkAlpha(IntroStartAlpha01);

            if (duration <= 0.0f)
            {
                SetDarkAlpha(0.0f);
                if (resolvedShadowStar != null)
                {
                    resolvedShadowStar.position = shadowStarTargetPosition;
                }

                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                if (fadeDuration <= 0.0f)
                {
                    SetDarkAlpha(0.0f);
                }
                else
                {
                    float fadeT = Mathf.Clamp01(elapsed / fadeDuration);
                    float fadeEased = Mathf.SmoothStep(0.0f, 1.0f, fadeT);
                    SetDarkAlpha(Mathf.LerpUnclamped(IntroStartAlpha01, 0.0f, fadeEased));
                }

                if (resolvedShadowStar != null)
                {
                    if (moveDuration <= 0.0f)
                    {
                        resolvedShadowStar.position = shadowStarTargetPosition;
                    }
                    else
                    {
                        float moveT = Mathf.Clamp01(elapsed / moveDuration);
                        float moveEased = Mathf.SmoothStep(0.0f, 1.0f, moveT);
                        resolvedShadowStar.position = Vector3.LerpUnclamped(
                            shadowStarStartPosition,
                            shadowStarTargetPosition,
                            moveEased);
                    }
                }

                yield return null;
            }

            SetDarkAlpha(0.0f);
            if (resolvedShadowStar != null)
            {
                resolvedShadowStar.position = shadowStarTargetPosition;
            }
        }

        private IEnumerator PlayIntroNarrationAndScrollBackgroundRoutine()
        {
            Transform resolvedBackground = ResolveBackgroundTransform();
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveIntroNarrationPlayer();
            if (narrationPlayer == null || narrationPlayer.StepCount == 0)
            {
                yield break;
            }

            int startIndex = Mathf.Clamp(introNarrationStartStepIndex, 0, narrationPlayer.StepCount);
            int stepCount = Mathf.Clamp(introNarrationStepCount, 0, narrationPlayer.StepCount - startIndex);
            if (stepCount <= 0)
            {
                yield break;
            }

            Vector3 startPosition = resolvedBackground != null ? resolvedBackground.position : Vector3.zero;
            Vector3 targetPosition = startPosition;
            targetPosition.x = introBackgroundScrollTargetX;

            yield return narrationPlayer.PlayRangeAndWaitRoutine(
                startIndex,
                stepCount,
                (stepIndex, elapsedSeconds, durationSeconds, deltaSeconds) =>
                {
                    if (resolvedBackground == null || durationSeconds <= 0.0f)
                    {
                        return;
                    }

                    float t = Mathf.Clamp01(elapsedSeconds / durationSeconds);
                    float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                    resolvedBackground.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                });

            if (resolvedBackground != null)
            {
                resolvedBackground.position = targetPosition;
            }
        }

        private IEnumerator PlayNextIntroNarrationRoutine()
        {
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveIntroNarrationPlayer();
            if (narrationPlayer == null || narrationPlayer.StepCount == 0)
            {
                yield break;
            }

            int firstPlayedIndex = Mathf.Clamp(introNarrationStartStepIndex, 0, narrationPlayer.StepCount);
            int playedStepCount = Mathf.Clamp(introNarrationStepCount, 0, narrationPlayer.StepCount - firstPlayedIndex);
            int nextStepIndex = firstPlayedIndex + playedStepCount;
            if (nextStepIndex >= narrationPlayer.StepCount)
            {
                yield break;
            }

            yield return narrationPlayer.PlayRangeAndWaitRoutine(nextStepIndex, 1);
        }

        private IEnumerator PlayPostFadeNarrationRoutine()
        {
            float delaySeconds = Mathf.Max(0.0f, postFadeNarrationDelaySeconds);
            if (delaySeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            NarrationSubtitleSequencePlayer narrationPlayer = ResolveIntroNarrationPlayer();
            if (narrationPlayer == null || narrationPlayer.StepCount == 0)
            {
                yield break;
            }

            int firstPlayedIndex = Mathf.Clamp(introNarrationStartStepIndex, 0, narrationPlayer.StepCount);
            int playedStepCount = Mathf.Clamp(introNarrationStepCount, 0, narrationPlayer.StepCount - firstPlayedIndex);
            int postFadeStepIndex = firstPlayedIndex + playedStepCount + 1;
            if (postFadeStepIndex >= narrationPlayer.StepCount)
            {
                yield break;
            }

            yield return narrationPlayer.PlayRangeAndWaitRoutine(postFadeStepIndex, 1);
        }

        private IEnumerator MoveShadowStarAlongRoadRoutine()
        {
            Transform resolvedShadowStar = ResolveShadowStarTransform();
            if (resolvedShadowStar == null)
            {
                yield break;
            }

            Vector3 startPosition = resolvedShadowStar.position;
            Vector2[] pathPoints = introShadowStarRoadPathPoints;
            if (pathPoints == null || pathPoints.Length == 0)
            {
                yield break;
            }

            float duration = Mathf.Max(0.0f, introShadowStarRoadMoveSeconds);
            Vector3 targetPosition = ToWorldPathPoint(pathPoints[pathPoints.Length - 1], startPosition.z);
            if (duration <= 0.0f)
            {
                resolvedShadowStar.position = targetPosition;
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                resolvedShadowStar.position = EvaluateRoadPath(startPosition, pathPoints, eased);
                yield return null;
            }

            resolvedShadowStar.position = targetPosition;
        }

        private Vector3 EvaluateRoadPath(Vector3 startPosition, Vector2[] pathPoints, float t)
        {
            if (pathPoints == null || pathPoints.Length == 0)
            {
                return startPosition;
            }

            float totalLength = 0.0f;
            Vector3 previousPoint = startPosition;
            for (int i = 0; i < pathPoints.Length; i++)
            {
                Vector3 nextPoint = ToWorldPathPoint(pathPoints[i], startPosition.z);
                totalLength += Vector3.Distance(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }

            if (totalLength <= 0.0001f)
            {
                return ToWorldPathPoint(pathPoints[pathPoints.Length - 1], startPosition.z);
            }

            float targetDistance = Mathf.Clamp01(t) * totalLength;
            previousPoint = startPosition;
            for (int i = 0; i < pathPoints.Length; i++)
            {
                Vector3 nextPoint = ToWorldPathPoint(pathPoints[i], startPosition.z);
                float segmentLength = Vector3.Distance(previousPoint, nextPoint);
                if (targetDistance <= segmentLength || i == pathPoints.Length - 1)
                {
                    float segmentT = segmentLength <= 0.0001f ? 1.0f : targetDistance / segmentLength;
                    return Vector3.LerpUnclamped(previousPoint, nextPoint, Mathf.Clamp01(segmentT));
                }

                targetDistance -= segmentLength;
                previousPoint = nextPoint;
            }

            return ToWorldPathPoint(pathPoints[pathPoints.Length - 1], startPosition.z);
        }

        private static Vector3 ToWorldPathPoint(Vector2 point, float z)
        {
            return new Vector3(point.x, point.y, z);
        }

        private IEnumerator FadeOutAndMoveShadowStarOffscreenRoutine()
        {
            Transform resolvedShadowStar = ResolveShadowStarTransform();
            Vector3 startPosition = resolvedShadowStar != null ? resolvedShadowStar.position : Vector3.zero;
            Vector3 targetPosition = resolvedShadowStar != null
                ? ResolveShadowStarBelowScreenPosition(resolvedShadowStar)
                : startPosition;
            float duration = Mathf.Max(0.0f, introFadeOutSeconds);

            if (duration <= 0.0f)
            {
                SetDarkAlpha(IntroStartAlpha01);
                if (resolvedShadowStar != null)
                {
                    resolvedShadowStar.position = targetPosition;
                }

                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetDarkAlpha(Mathf.LerpUnclamped(0.0f, IntroStartAlpha01, t));
                if (resolvedShadowStar != null)
                {
                    resolvedShadowStar.position = Vector3.LerpUnclamped(startPosition, targetPosition, t);
                }

                yield return null;
            }

            SetDarkAlpha(IntroStartAlpha01);
            if (resolvedShadowStar != null)
            {
                resolvedShadowStar.position = targetPosition;
            }
        }

        private IEnumerator LoadHologramSceneRoutine()
        {
            if (string.IsNullOrWhiteSpace(hologramSceneName))
            {
                loadRoutine = null;
                yield break;
            }

            int resolvedDisplay = ResolveTargetDisplayIndex();
            ActivateTargetDisplay(resolvedDisplay);

            Scene hologramScene = SceneManager.GetSceneByName(hologramSceneName);
            if (!hologramScene.IsValid() || !hologramScene.isLoaded)
            {
                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(hologramSceneName, LoadSceneMode.Additive);
                if (loadOperation == null)
                {
                    Debug.LogWarning($"Mission3HologramDisplayLoader: scene could not be loaded: {hologramSceneName}");
                    loadRoutine = null;
                    yield break;
                }

                while (!loadOperation.isDone)
                {
                    yield return null;
                }
            }

            ApplyHologramTargetDisplay(resolvedDisplay);
            yield return null;
            ApplyHologramTargetDisplay(resolvedDisplay);
            loadRoutine = null;
        }

        private int ResolveTargetDisplayIndex()
        {
            if (Display.displays == null || Display.displays.Length == 0)
            {
                return 0;
            }

            return Mathf.Clamp(DisplayRoutingSettings.HologramUnityDisplayIndex, 0, Display.displays.Length - 1);
        }

        private static void ActivateTargetDisplay(int displayIndex)
        {
            if (displayIndex > 0 && displayIndex < Display.displays.Length)
            {
                Display.displays[displayIndex].Activate();
            }
        }

        private void ApplyHologramTargetDisplay(int displayIndex)
        {
            Scene hologramScene = SceneManager.GetSceneByName(hologramSceneName);
            if (!hologramScene.IsValid() || !hologramScene.isLoaded)
            {
                return;
            }

            foreach (GameObject rootObject in hologramScene.GetRootGameObjects())
            {
                Camera[] cameras = rootObject.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameras[i].targetDisplay = displayIndex;
                }

                Canvas[] canvases = rootObject.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].targetDisplay = displayIndex;
                }
            }
        }

        private float IntroStartAlpha01 => Mathf.Clamp(introStartAlpha, 0, 255) / 255.0f;

        private Renderer ResolveDarkRenderer()
        {
            if (darkRenderer != null)
            {
                return darkRenderer;
            }

            GameObject darkObject = GameObject.Find("dark");
            if (darkObject != null)
            {
                darkRenderer = darkObject.GetComponent<Renderer>();
            }

            return darkRenderer;
        }

        private Transform ResolveShadowStarTransform()
        {
            if (shadowStarTransform != null)
            {
                return shadowStarTransform;
            }

            GameObject shadowStarObject = GameObject.Find("ShadowStar");
            if (shadowStarObject != null)
            {
                shadowStarTransform = shadowStarObject.transform;
            }

            return shadowStarTransform;
        }

        private Transform ResolveBackgroundTransform()
        {
            if (backgroundTransform != null)
            {
                return backgroundTransform;
            }

            GameObject backgroundObject = GameObject.Find("Background");
            if (backgroundObject != null)
            {
                backgroundTransform = backgroundObject.transform;
            }

            return backgroundTransform;
        }

        private Camera ResolveTargetCamera()
        {
            if (targetCamera != null && targetCamera.isActiveAndEnabled)
            {
                return targetCamera;
            }

            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = FindObjectOfType<Camera>();
            }

            return targetCamera;
        }

        private Vector3 ResolveShadowStarBelowScreenPosition(Transform shadowStar)
        {
            Vector3 position = shadowStar.position;
            Camera camera = ResolveTargetCamera();
            if (camera == null)
            {
                position.y -= Mathf.Max(0.0f, shadowStarOffscreenFallbackDistance);
                return position;
            }

            float planeDistance = Mathf.Abs(Vector3.Dot(
                position - camera.transform.position,
                camera.transform.forward));
            planeDistance = Mathf.Max(camera.nearClipPlane, planeDistance);
            Vector3 bottomEdge = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.0f, planeDistance));
            Renderer renderer = shadowStar.GetComponentInChildren<Renderer>();
            float halfHeight = renderer != null && renderer.bounds.size.y > 0.0f
                ? renderer.bounds.extents.y
                : 0.5f;
            position.y = bottomEdge.y - halfHeight - Mathf.Max(0.0f, shadowStarOffscreenPaddingWorld);
            return position;
        }

        private NarrationSubtitleSequencePlayer ResolveIntroNarrationPlayer()
        {
            if (introNarrationPlayer != null)
            {
                return introNarrationPlayer;
            }

            introNarrationPlayer = GetComponent<NarrationSubtitleSequencePlayer>();
            if (introNarrationPlayer == null)
            {
                introNarrationPlayer = FindObjectOfType<NarrationSubtitleSequencePlayer>();
            }

            return introNarrationPlayer;
        }

        private void SetDarkAlpha(float alpha)
        {
            Renderer targetRenderer = ResolveDarkRenderer();
            if (targetRenderer == null)
            {
                return;
            }

            if (darkPropertyBlock == null)
            {
                darkPropertyBlock = new MaterialPropertyBlock();
            }

            Color color = Color.black;
            color.a = Mathf.Clamp01(alpha);
            targetRenderer.GetPropertyBlock(darkPropertyBlock);
            darkPropertyBlock.SetColor(BaseColorId, color);
            darkPropertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(darkPropertyBlock);
        }
    }
}
