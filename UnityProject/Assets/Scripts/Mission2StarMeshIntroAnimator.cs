using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public sealed class Mission2StarMeshIntroAnimator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public enum Mission2Phase
        {
            Intro,
            Interaction,
            Outro
        }

        [Header("Phase")]
        [SerializeField] private Mission2Phase initialPhase = Mission2Phase.Intro;
        [SerializeField] private bool autoEnterInteractionAfterIntro = true;

        [Header("Flow")]
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool setMission2StateOnStart = true;

        [Header("Dark Fade")]
        [SerializeField] private Renderer darkRenderer;
        [SerializeField, Range(0, 255)] private int darkStartAlpha = 230;
        [SerializeField, Min(0.0f)] private float darkFadeSeconds = 2.0f;

        [Header("Background Fade")]
        [SerializeField] private Renderer introBackgroundRenderer;
        [SerializeField, Range(0, 255)] private int introBackgroundStartAlpha = 230;
        [SerializeField, Min(0.0f)] private float introBackgroundFadeOutSeconds = 2.0f;
        [SerializeField, Min(0.0f)] private float outroBackgroundFadeInSeconds = 2.0f;

        [Header("Sun Intro")]
        [SerializeField] private Transform introSunTransform;
        [SerializeField] private float introSunTargetX = 7.5f;

        [Header("Narration")]
        [SerializeField] private NarrationSubtitleSequencePlayer introNarrationPlayer;
        [SerializeField, Min(0)] private int introNarrationStartStepIndex;
        [SerializeField, Min(0)] private int introNarrationStepCount = 6;

        [Header("Shadow Star Narration Motion")]
        [SerializeField] private Transform shadowStarTransform;
        [SerializeField] private float shadowStarDropTargetY = -3.3f;
        [SerializeField, Min(0.0f)] private float shadowStarDropSeconds = 0.18f;
        [SerializeField] private float shadowStarAfterNarrationTargetX = -4.0f;
        [SerializeField, Min(0.0f)] private float shadowStarAfterNarrationMoveSeconds = 7.0f;

        [Header("Outro")]
        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(0)] private int outroNarrationStartStepIndex = 6;
        [SerializeField, Min(0)] private int outroNarrationStepCount = 2;
        [SerializeField, Min(0.0f)] private float outroShadowStarExitSeconds = 3.0f;
        [SerializeField, Min(0.0f)] private float outroShadowStarExitPaddingWorld = 1.0f;
        [SerializeField, Range(0, 255)] private int outroSceneFadeOutAlpha = 255;
        [SerializeField, Min(0.0f)] private float outroSceneFadeOutSeconds = 2.0f;

        [Header("Interaction Systems")]
        [SerializeField] private Mission2SunHandSystem sunHandSystem;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private MediaPipeTrackingProcessLauncher mediaPipeLauncher;
        [SerializeField] private bool prewarmMediaPipeDuringIntro = true;

        [Header("Interaction UI")]
        [SerializeField] private string interactionInstructionText = "\uADF8\uB9BC\uC790\uB97C \uD0A4\uC6B0\uB824\uBA74...?";
        [SerializeField] private Color interactionInstructionTextColor = Color.black;
        [SerializeField, Min(12)] private int interactionInstructionFontSize = 36;
        [SerializeField, Range(0.2f, 1.0f)] private float interactionInstructionWidthRatio = 0.9f;
        [SerializeField] private GameObject interactionInstructionObject;
        [SerializeField] private Text interactionInstructionTextComponent;

        private Coroutine animationRoutine;
        private Mission2Phase currentPhase;
        private MaterialPropertyBlock darkPropertyBlock;
        private MaterialPropertyBlock backgroundPropertyBlock;

        public Mission2Phase CurrentPhase => currentPhase;

        private void Awake()
        {
            currentPhase = initialPhase;
            ResolveDarkRenderer();
            ResolveIntroBackgroundRenderer();
            ResolveIntroSunTransform();
            ResolveNarrationPlayer();
            SetDarkAlpha(currentPhase == Mission2Phase.Intro ? DarkStartAlpha01 : 0.0f);
            SetIntroBackgroundAlpha(currentPhase == Mission2Phase.Intro ? IntroBackgroundStartAlpha01 : 0.0f);
            SetInteractionSystemsEnabled(false);
            SetInteractionInstructionVisible(currentPhase == Mission2Phase.Interaction);
        }

        private void Start()
        {
            if (setMission2StateOnStart)
            {
                FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission2);
            }

            if (currentPhase == Mission2Phase.Interaction)
            {
                EnterInteraction();
                return;
            }

            if (currentPhase == Mission2Phase.Outro)
            {
                EnterOutro();
                return;
            }

            EnterIntro();
            if (playOnStart)
            {
                Play();
            }
        }

        public void Play()
        {
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
            }

            EnterIntro();
            animationRoutine = StartCoroutine(PlayIntroSequenceRoutine());
        }

        private IEnumerator PlayIntroSequenceRoutine()
        {
            yield return FadeDarkRoutine();
            yield return PlayIntroNarrationRoutine();
            yield return FadeIntroBackgroundOutRoutine();

            if (autoEnterInteractionAfterIntro)
            {
                EnterInteraction();
            }

            animationRoutine = null;
        }

        public void EnterIntro()
        {
            currentPhase = Mission2Phase.Intro;
            ResolveDarkRenderer();
            ResolveIntroBackgroundRenderer();
            ResolveIntroSunTransform();
            SetDarkAlpha(DarkStartAlpha01);
            SetIntroBackgroundAlpha(IntroBackgroundStartAlpha01);
            SetInteractionSystemsEnabled(false);
            if (prewarmMediaPipeDuringIntro)
            {
                StartMediaPipeTracking();
            }

            SetInteractionInstructionVisible(false);
        }

        public void EnterInteraction()
        {
            currentPhase = Mission2Phase.Interaction;
            SetDarkAlpha(0.0f);
            SetIntroBackgroundAlpha(0.0f);
            SetInteractionInstructionVisible(true);
            StartMediaPipeTracking();

            if (sunHandSystem != null)
            {
                sunHandSystem.BeginInteraction();
            }
        }

        public void HideInteractionInstruction()
        {
            SetInteractionInstructionVisible(false);
        }

        public void EnterOutro()
        {
            currentPhase = Mission2Phase.Outro;
            SetInteractionInstructionVisible(false);
            SetInteractionSystemsEnabled(false);
        }

        public IEnumerator EnterOutroAndWaitRoutine()
        {
            EnterOutro();
            yield return FadeOutroBackgroundInRoutine();
            yield return PlayOutroNarrationRoutine();
            yield return MoveShadowStarLeftOffscreenRoutine();
            yield return FadeSceneToBlackRoutine();
        }

        private void SetInteractionSystemsEnabled(bool isEnabled)
        {
            if (sunHandSystem != null)
            {
                sunHandSystem.enabled = isEnabled;
            }

            if (mediaPipeReceiver != null)
            {
                mediaPipeReceiver.enabled = isEnabled;
            }

            if (mediaPipeLauncher != null)
            {
                mediaPipeLauncher.enabled = isEnabled;
            }
        }

        private void StartMediaPipeTracking()
        {
            if (mediaPipeReceiver != null)
            {
                mediaPipeReceiver.enabled = true;
                mediaPipeReceiver.StartReceiver();
            }

            if (mediaPipeLauncher != null)
            {
                mediaPipeLauncher.enabled = true;
                mediaPipeLauncher.Launch();
            }
        }

        private void SetInteractionInstructionVisible(bool isVisible)
        {
            ResolveInteractionInstructionReferences();

            if (isVisible)
            {
                ApplyInteractionInstructionSettings();
            }

            if (interactionInstructionObject != null)
            {
                interactionInstructionObject.SetActive(isVisible);
            }
            else if (interactionInstructionTextComponent != null)
            {
                interactionInstructionTextComponent.gameObject.SetActive(isVisible);
            }
        }

        private void ResolveInteractionInstructionReferences()
        {
            if (interactionInstructionTextComponent == null && interactionInstructionObject != null)
            {
                interactionInstructionTextComponent = interactionInstructionObject.GetComponentInChildren<Text>(true);
            }

            if (interactionInstructionObject == null && interactionInstructionTextComponent != null)
            {
                interactionInstructionObject = interactionInstructionTextComponent.gameObject;
            }
        }

        private void ApplyInteractionInstructionSettings()
        {
            if (interactionInstructionTextComponent == null)
            {
                return;
            }

            RectTransform rectTransform = interactionInstructionTextComponent.GetComponent<RectTransform>();

            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.sizeDelta = new Vector2(1920.0f * Mathf.Clamp01(interactionInstructionWidthRatio), 80.0f);
            }

            interactionInstructionTextComponent.text = interactionInstructionText;
            interactionInstructionTextComponent.color = interactionInstructionTextColor;
            interactionInstructionTextComponent.fontSize = Mathf.Max(12, interactionInstructionFontSize);
            interactionInstructionTextComponent.alignment = TextAnchor.MiddleCenter;
            interactionInstructionTextComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            interactionInstructionTextComponent.verticalOverflow = VerticalWrapMode.Truncate;
            interactionInstructionTextComponent.raycastTarget = false;
            interactionInstructionTextComponent.supportRichText = false;
            interactionInstructionTextComponent.font = ResolveInteractionInstructionFont();
        }

        private static Font ResolveInteractionInstructionFont()
        {
            Font resourceFont = Resources.Load<Font>("Fonts/KoPubWorld Batang Medium");
            if (resourceFont != null)
            {
                return resourceFont;
            }

            return Font.CreateDynamicFontFromOSFont(
                new[] { "KoPubWorld Batang Medium", "Malgun Gothic", "Arial" },
                42);
        }

        private IEnumerator FadeDarkRoutine()
        {
            float duration = Mathf.Max(0.0f, darkFadeSeconds);
            Transform sunTransform = ResolveIntroSunTransform();
            Vector3 sunStartPosition = sunTransform != null ? sunTransform.position : Vector3.zero;
            Vector3 sunTargetPosition = sunStartPosition;
            sunTargetPosition.x = introSunTargetX;

            SetDarkAlpha(DarkStartAlpha01);

            if (duration <= 0.0f)
            {
                SetDarkAlpha(0.0f);
                if (sunTransform != null)
                {
                    sunTransform.position = sunTargetPosition;
                }

                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                SetDarkAlpha(Mathf.LerpUnclamped(DarkStartAlpha01, 0.0f, eased));

                if (sunTransform != null)
                {
                    sunTransform.position = Vector3.LerpUnclamped(sunStartPosition, sunTargetPosition, eased);
                }

                yield return null;
            }

            SetDarkAlpha(0.0f);
            if (sunTransform != null)
            {
                sunTransform.position = sunTargetPosition;
            }
        }

        private IEnumerator PlayIntroNarrationRoutine()
        {
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveNarrationPlayer();
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

            yield return narrationPlayer.PlayRangeAndWaitRoutine(startIndex, 1);
            yield return MoveShadowStarYRoutine(shadowStarDropTargetY, shadowStarDropSeconds, easeIn: true);

            if (stepCount <= 1)
            {
                yield break;
            }

            yield return narrationPlayer.PlayRangeAndWaitRoutine(startIndex + 1, 1);
            yield return MoveShadowStarXRoutine(
                shadowStarAfterNarrationTargetX,
                shadowStarAfterNarrationMoveSeconds,
                easeIn: false);

            if (stepCount > 2)
            {
                yield return narrationPlayer.PlayRangeAndWaitRoutine(startIndex + 2, stepCount - 2);
            }
        }

        private IEnumerator FadeIntroBackgroundOutRoutine()
        {
            Renderer backgroundRenderer = ResolveIntroBackgroundRenderer();
            if (backgroundRenderer == null)
            {
                yield break;
            }

            float duration = Mathf.Max(0.0f, introBackgroundFadeOutSeconds);
            if (duration <= 0.0f)
            {
                SetIntroBackgroundAlpha(0.0f);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                SetIntroBackgroundAlpha(Mathf.LerpUnclamped(IntroBackgroundStartAlpha01, 0.0f, eased));
                yield return null;
            }

            SetIntroBackgroundAlpha(0.0f);
        }

        private IEnumerator FadeOutroBackgroundInRoutine()
        {
            Renderer backgroundRenderer = ResolveIntroBackgroundRenderer();
            if (backgroundRenderer == null)
            {
                yield break;
            }

            float duration = Mathf.Max(0.0f, outroBackgroundFadeInSeconds);
            if (duration <= 0.0f)
            {
                SetIntroBackgroundAlpha(IntroBackgroundStartAlpha01);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                SetIntroBackgroundAlpha(Mathf.LerpUnclamped(0.0f, IntroBackgroundStartAlpha01, eased));
                yield return null;
            }

            SetIntroBackgroundAlpha(IntroBackgroundStartAlpha01);
        }

        private IEnumerator PlayOutroNarrationRoutine()
        {
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveNarrationPlayer();
            if (narrationPlayer == null || narrationPlayer.StepCount == 0)
            {
                yield break;
            }

            int startIndex = Mathf.Clamp(outroNarrationStartStepIndex, 0, narrationPlayer.StepCount);
            int stepCount = Mathf.Clamp(outroNarrationStepCount, 0, narrationPlayer.StepCount - startIndex);
            if (stepCount <= 0)
            {
                yield break;
            }

            yield return narrationPlayer.PlayRangeAndWaitRoutine(startIndex, stepCount);
        }

        private IEnumerator MoveShadowStarLeftOffscreenRoutine()
        {
            Transform resolvedShadowStar = ResolveShadowStarTransform();
            if (resolvedShadowStar == null)
            {
                yield break;
            }

            Vector3 targetPosition = ResolveShadowStarLeftOffscreenPosition(resolvedShadowStar);
            yield return MoveShadowStarToPositionRoutine(
                resolvedShadowStar,
                targetPosition,
                outroShadowStarExitSeconds);
        }

        private IEnumerator MoveShadowStarToPositionRoutine(
            Transform targetTransform,
            Vector3 targetPosition,
            float durationSeconds)
        {
            if (targetTransform == null)
            {
                yield break;
            }

            Vector3 startPosition = targetTransform.position;
            float duration = Mathf.Max(0.0f, durationSeconds);
            if (duration <= 0.0f)
            {
                targetTransform.position = targetPosition;
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                targetTransform.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                yield return null;
            }

            targetTransform.position = targetPosition;
        }

        private Vector3 ResolveShadowStarLeftOffscreenPosition(Transform shadowStar)
        {
            Vector3 position = shadowStar.position;
            Camera camera = ResolveTargetCamera();
            if (camera == null)
            {
                return position;
            }

            float planeDistance = Mathf.Abs(Vector3.Dot(
                position - camera.transform.position,
                camera.transform.forward));
            planeDistance = Mathf.Max(camera.nearClipPlane, planeDistance);
            Vector3 leftEdge = camera.ViewportToWorldPoint(new Vector3(0.0f, 0.5f, planeDistance));
            Renderer renderer = shadowStar.GetComponentInChildren<Renderer>();
            float halfWidth = renderer != null && renderer.bounds.size.x > 0.0f
                ? renderer.bounds.extents.x
                : 0.5f;
            position.x = leftEdge.x - halfWidth - Mathf.Max(0.0f, outroShadowStarExitPaddingWorld);
            return position;
        }

        private IEnumerator FadeSceneToBlackRoutine()
        {
            float duration = Mathf.Max(0.0f, outroSceneFadeOutSeconds);
            float targetAlpha = Mathf.Clamp(outroSceneFadeOutAlpha, 0, 255) / 255.0f;
            if (duration <= 0.0f)
            {
                SetDarkAlpha(targetAlpha);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                SetDarkAlpha(Mathf.LerpUnclamped(0.0f, targetAlpha, eased));
                yield return null;
            }

            SetDarkAlpha(targetAlpha);
        }

        private IEnumerator MoveShadowStarYRoutine(float targetY, float durationSeconds, bool easeIn)
        {
            Transform resolvedShadowStar = ResolveShadowStarTransform();
            if (resolvedShadowStar == null)
            {
                yield break;
            }

            Vector3 startPosition = resolvedShadowStar.position;
            Vector3 targetPosition = startPosition;
            targetPosition.y = targetY;

            float duration = Mathf.Max(0.0f, durationSeconds);
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
                float eased = easeIn ? t * t : Mathf.SmoothStep(0.0f, 1.0f, t);
                resolvedShadowStar.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                yield return null;
            }

            resolvedShadowStar.position = targetPosition;
        }

        private IEnumerator MoveShadowStarXRoutine(float targetX, float durationSeconds, bool easeIn)
        {
            Transform resolvedShadowStar = ResolveShadowStarTransform();
            if (resolvedShadowStar == null)
            {
                yield break;
            }

            Vector3 startPosition = resolvedShadowStar.position;
            Vector3 targetPosition = startPosition;
            targetPosition.x = targetX;

            float duration = Mathf.Max(0.0f, durationSeconds);
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
                float eased = easeIn ? t * t : Mathf.SmoothStep(0.0f, 1.0f, t);
                resolvedShadowStar.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                yield return null;
            }

            resolvedShadowStar.position = targetPosition;
        }

        private float DarkStartAlpha01 => Mathf.Clamp(darkStartAlpha, 0, 255) / 255.0f;

        private float IntroBackgroundStartAlpha01 => Mathf.Clamp(introBackgroundStartAlpha, 0, 255) / 255.0f;

        private Renderer ResolveDarkRenderer()
        {
            return darkRenderer;
        }

        private Renderer ResolveIntroBackgroundRenderer()
        {
            return introBackgroundRenderer;
        }

        private Transform ResolveIntroSunTransform()
        {
            return introSunTransform;
        }

        private Camera ResolveTargetCamera()
        {
            return targetCamera;
        }

        private NarrationSubtitleSequencePlayer ResolveNarrationPlayer()
        {
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

        private void SetIntroBackgroundAlpha(float alpha)
        {
            Renderer targetRenderer = ResolveIntroBackgroundRenderer();
            if (targetRenderer == null)
            {
                return;
            }

            if (backgroundPropertyBlock == null)
            {
                backgroundPropertyBlock = new MaterialPropertyBlock();
            }

            Color color = Color.white;
            color.a = Mathf.Clamp01(alpha);
            targetRenderer.GetPropertyBlock(backgroundPropertyBlock);
            backgroundPropertyBlock.SetColor(BaseColorId, color);
            backgroundPropertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(backgroundPropertyBlock);
        }

        private Transform ResolveShadowStarTransform()
        {
            return shadowStarTransform;
        }
    }
}
