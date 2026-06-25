using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public sealed class Mission1Controller : MonoBehaviour
    {
        public enum Mission1Phase
        {
            Intro,
            Interaction,
            Outro
        }

        private const int StarPointCount = 10;
        private const int IntroBackgroundScrollStepCount = 2;
        private const int IntroDarkFadeStepIndex = 1;
        private const int IntroStarRevealStepIndex = 3;
        private const int SceneTransitionBackgroundDestroyFrames = 1;
        private const float DefaultOuterRadiusViewportY = 0.2777778f;
        private const float DefaultInnerRadiusViewportY = 0.11851852f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private ShadowMeshDeformer targetMeshDeformer;
        [SerializeField] private SceneFlowController sceneFlowController;
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool centerMeshOnMissionStart = true;

        [Header("Phase")]
        [SerializeField] private Mission1Phase initialPhase = Mission1Phase.Intro;
        [SerializeField] private bool autoEnterInteractionOnStart;
        [SerializeField, Min(0.0f)] private float introDurationSeconds;

        [Header("Intro")]
        [SerializeField] private GameObject[] introObjects = Array.Empty<GameObject>();
        [SerializeField] private bool hideIntroObjectsOnInteraction = true;
        [SerializeField] private Renderer[] introFadeRenderers = Array.Empty<Renderer>();
        [SerializeField] private Transform introShadowRoot;
        [SerializeField] private Vector2 introShadowRootTargetPosition = new Vector2(-3.0f, -3.0f);
        [SerializeField, Min(0.01f)] private float introShadowRootTargetScale = 0.7f;
        [SerializeField, Min(0.0f)] private float introRevealSeconds = 2.0f;
        [SerializeField, Range(0, 255)] private int introBackgroundFinalAlpha = 230;
        [SerializeField] private Transform introBackgroundTransform;
        [SerializeField] private NarrationSubtitleSequencePlayer introNarrationPlayer;
        [SerializeField, Min(0)] private int introNarrationStepCount = 5;
        [SerializeField] private float introBackgroundScrollTargetX = -10.42593f;
        [SerializeField] private Renderer introDarkRenderer;
        [SerializeField, Range(0, 255)] private int introDarkFinalAlpha = 230;
        [SerializeField] private Renderer[] introStarRenderers = Array.Empty<Renderer>();
        [SerializeField, Min(0)] private int introStarRevealNarrationStepIndex = IntroStarRevealStepIndex;
        [SerializeField, Min(0.01f)] private float introStarRevealSeconds = 2.0f;
        [SerializeField, Range(0, 255)] private int introStarFinalAlpha = 255;
        [SerializeField, Min(0.0f)] private float introToInteractionTransitionSeconds = 2.0f;
        [SerializeField, Min(0.01f)] private float introStarInteractionScale = 2.8f;

        [Header("Interaction Systems")]
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private MediaPipeMeshDeformationInput deformationInput;
        [SerializeField] private MediaPipeInteractionVisualizer interactionVisualizer;
        [SerializeField] private MediaPipeTrackingProcessLauncher mediaPipeLauncher;
        [SerializeField] private bool prewarmMediaPipeDuringIntro = true;

        [Header("Guide")]
        [SerializeField] private bool createGuideOverlay = true;
        [SerializeField, Min(1)] private int guideLineWidthPixels = 4;
        [SerializeField, Min(1)] private int guideDashLengthPixels = 12;
        [SerializeField, Min(1)] private int guideGapLengthPixels = 7;
        [SerializeField] private Color guideColor = new Color(0.68f, 0.68f, 0.68f, 0.96f);
        [SerializeField, Range(0.05f, 0.45f)] private float outerRadiusViewportY = DefaultOuterRadiusViewportY;
        [SerializeField, Range(0.01f, 0.3f)] private float innerRadiusViewportY = DefaultInnerRadiusViewportY;
        [FormerlySerializedAs("guideDepthOffsetBehindMesh")]
        [SerializeField, Min(0.0f)] private float guideDepthOffsetInFrontOfMesh = 0.08f;
        [SerializeField] private int guideSortingOrder = 100;

        [Header("Match")]
        [SerializeField, Range(0.01f, 1.0f)] private float matchThreshold = 0.9f;
        [SerializeField, Min(0.0f)] private float requiredHoldSeconds = 1.0f;
        [SerializeField, Range(24, 160)] private int sampleRows = 72;
        [SerializeField, Min(0.02f)] private float evaluationIntervalSeconds = 0.1f;
        [SerializeField] private string nextSceneName = "Mission2";

        [Header("Completion")]
        [SerializeField, Min(0.0f)] private float completionEffectSeconds = 2.0f;
        [SerializeField, Min(0.0f)] private float starMorphSeconds = 2.0f;
        [SerializeField] private bool replaceMeshWithStarOnComplete = true;
        [SerializeField] private GameObject shadowStarObject;
        [SerializeField, Range(0.0f, 1.0f)] private float completionDingVolume = 0.85f;
        [SerializeField, Min(0.05f)] private float particleBurstSeconds = 2.0f;

        [Header("Outro")]
        [SerializeField, Min(0.0f)] private float outroReturnSeconds = 2.0f;
        [SerializeField] private Vector2 outroShadowStarTargetPosition = new Vector2(0.95f, -2.8f);
        [SerializeField, Min(0.01f)] private float outroShadowStarTargetScale = 0.8f;

        [Header("Scene Transition")]
        [SerializeField, Min(0.0f)] private float mission2SceneTransitionDelaySeconds = 1.0f;

        [Header("Skip")]
        [SerializeField] private bool enableInteractionSkipKey = true;
        [SerializeField] private KeyCode interactionSkipKey = KeyCode.S;

        [Header("Scene UI")]
        [SerializeField] private RectTransform matchProgressFillRect;
        [SerializeField] private RectTransform matchThresholdMarker;
        [SerializeField] private string interactionInstructionText = "\uADF8\uB9BC\uC790\uB97C \uD615\uD0DC\uC5D0 \uB9DE\uCDB0\uC8FC\uC138\uC694.";
        [SerializeField] private Color interactionInstructionTextColor = Color.black;
        [SerializeField, Min(12)] private int interactionInstructionFontSize = 36;
        [SerializeField, Min(0.0f)] private float interactionInstructionTopMargin = 56.0f;
        [SerializeField, Range(0.2f, 1.0f)] private float interactionInstructionWidthRatio = 0.9f;
        [SerializeField] private GameObject interactionInstructionObject;
        [SerializeField] private Text interactionInstructionTextComponent;
        [SerializeField] private GameObject preInteractionTutorialObject;
        [SerializeField, Min(0.0f)] private float preInteractionTutorialSeconds = 4.0f;

        private GameObject guideLineObject;
        private Material guideLineMaterial;
        private readonly List<LineRenderer> guideDashRenderers = new List<LineRenderer>();
        private Vector2[] guidePolygon = Array.Empty<Vector2>();
        private float guidePolygonAspect;
        private float nextEvaluationTime;
        private float lastEvaluationTime;
        private float matchedSeconds;
        private bool missionCompleted;
        private bool centeredMeshInMission;
        private bool interactionStarted;
        private Mission1Phase currentPhase;
        private AudioClip completionDingClip;
        private Material sparkleMaterial;
        private MaterialPropertyBlock introPropertyBlock;
        private Coroutine introRevealRoutine;
        private bool hasIntroShadowRootRestPose;
        private Vector3 introShadowRootRestLocalScale = Vector3.one;
        private Quaternion introShadowRootRestLocalRotation = Quaternion.identity;
        private bool hasIntroStarRestPose;
        private Vector3 introStarRestLocalPosition = new Vector3(0.95f, -2.8f, 0.0f);
        private Quaternion introStarRestLocalRotation = Quaternion.identity;
        private Vector3 introStarRestLocalScale = new Vector3(0.8f, 0.8f, 0.8f);
        private GameObject recreatedIntroStarObject;
        private Mesh recreatedIntroStarMesh;
        private Material recreatedIntroStarMaterial;

        public Mission1Phase CurrentPhase => currentPhase;
        public bool IsInteractionSkippable => currentPhase == Mission1Phase.Interaction && interactionStarted && !missionCompleted;
        public float LastMatchScore { get; private set; }

        private void Awake()
        {
            currentPhase = initialPhase;
            ResolveRuntimeReferences();
            RebuildGuidePolygonIfNeeded(force: true);

            ResetInteractionProgress();
            SetInteractionSystemsEnabled(false);
            SetMatchProgressBarVisible(false);
            SetIntroObjectsVisible(currentPhase == Mission1Phase.Intro);
            SetIntroBackgroundAlpha(currentPhase == Mission1Phase.Intro ? 0.0f : IntroBackgroundFinalAlpha01);
            SetIntroDarkAlpha(0.0f);
            SetIntroStarAlpha(currentPhase == Mission1Phase.Intro ? 0.0f : IntroStarFinalAlpha01);
            SetShadowStarVisible(false);
            SetInteractionInstructionVisible(currentPhase == Mission1Phase.Interaction);
            SetPreInteractionTutorialVisible(false);
        }

        private IEnumerator Start()
        {
            if (currentPhase == Mission1Phase.Interaction)
            {
                EnterInteraction();
                yield break;
            }

            if (currentPhase == Mission1Phase.Outro)
            {
                EnterOutro();
                yield break;
            }

            EnterIntro();
            if (!autoEnterInteractionOnStart)
            {
                yield break;
            }

            if (introDurationSeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(introDurationSeconds);
            }

            yield return PlayPreInteractionTutorialRoutine();
            EnterInteraction();
        }

        private void Update()
        {
            if (!IsInteractionSkippable)
            {
                return;
            }

            if (enableInteractionSkipKey &&
                interactionSkipKey != KeyCode.None &&
                Input.GetKeyDown(interactionSkipKey))
            {
                SkipInteraction();
                return;
            }

            float now = Time.unscaledTime;
            if (now < nextEvaluationTime)
            {
                return;
            }

            float elapsedSinceLastEvaluation = lastEvaluationTime > 0.0f
                ? now - lastEvaluationTime
                : evaluationIntervalSeconds;
            lastEvaluationTime = now;
            nextEvaluationTime = now + Mathf.Max(0.02f, evaluationIntervalSeconds);

            if (!ResolveRuntimeReferences() || targetMeshDeformer == null || !targetMeshDeformer.HasMesh)
            {
                matchedSeconds = 0.0f;
                LastMatchScore = 0.0f;
                UpdateMatchProgressBar(0.0f);
                return;
            }

            CenterMeshForMissionIfNeeded();
            RebuildGuidePolygonIfNeeded(force: false);
            UpdateGuideLine();
            LastMatchScore = EvaluateMatchScore();
            UpdateMatchProgressBar(LastMatchScore);

            if (LastMatchScore < matchThreshold)
            {
                matchedSeconds = 0.0f;
                return;
            }

            matchedSeconds += Mathf.Max(0.0f, elapsedSinceLastEvaluation);
            if (matchedSeconds >= requiredHoldSeconds)
            {
                CompleteMission();
            }
        }

        public void EnterIntro()
        {
            currentPhase = Mission1Phase.Intro;
            interactionStarted = false;
            centeredMeshInMission = false;
            SetIntroObjectsVisible(true);
            SetIntroBackgroundAlpha(0.0f);
            SetIntroDarkAlpha(0.0f);
            SetIntroStarAlpha(0.0f);
            SetInteractionSystemsEnabled(false);
            if (prewarmMediaPipeDuringIntro)
            {
                StartMediaPipeTracking();
            }

            SetGuideVisible(false);
            SetMatchProgressBarVisible(false);
            SetInteractionInstructionVisible(false);
            SetPreInteractionTutorialVisible(false);
            ResetInteractionProgress();
            StartIntroReveal();
        }

        public void EnterInteraction()
        {
            if (missionCompleted || interactionStarted)
            {
                return;
            }

            currentPhase = Mission1Phase.Interaction;
            interactionStarted = true;

            StopIntroReveal();
            SetPreInteractionTutorialVisible(false);

            ResolveRuntimeReferences();
            RebuildGuidePolygonIfNeeded(force: true);
            ResetInteractionProgress();
            SetInteractionSystemsEnabled(true);
            StartMediaPipeTracking();
            SetMatchProgressBarVisible(true);

            if (createGuideOverlay && guideLineObject == null)
            {
                CreateGuideLine();
            }

            SetGuideVisible(createGuideOverlay);
            UpdateGuideLine();
            DestroyIntroStarObject(hideGuide: false);
            SetInteractionInstructionVisible(true);

            if (hideIntroObjectsOnInteraction)
            {
                SetIntroObjectsVisible(false);
            }

            stateManager?.SetState(GameStateManager.PipelineState.Mission1);
        }

        public void EnterOutro()
        {
            currentPhase = Mission1Phase.Outro;
            interactionStarted = false;
            SetInteractionSystemsEnabled(false);
            SetGuideVisible(false);
            HideMatchProgressBar();
            SetInteractionInstructionVisible(false);
            SetPreInteractionTutorialVisible(false);
            StopIntroNarrationPlayback();
        }

        public void DebugAdvance()
        {
            if (currentPhase == Mission1Phase.Intro)
            {
                EnterInteraction();
                return;
            }

            if (currentPhase == Mission1Phase.Interaction)
            {
                CompleteMission();
                return;
            }

            LoadNextScene();
        }

        public void SkipInteraction()
        {
            if (!IsInteractionSkippable)
            {
                return;
            }

            CompleteMission();
        }

        private bool ResolveRuntimeReferences()
        {
            if (targetMeshDeformer == null)
            {
                targetMeshDeformer = FindObjectOfType<ShadowMeshDeformer>();
            }

            if (sceneFlowController == null)
            {
                sceneFlowController = FindObjectOfType<SceneFlowController>();
            }

            if (stateManager == null)
            {
                stateManager = FindObjectOfType<GameStateManager>();
            }

            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                return false;
            }

            return targetMeshDeformer != null && targetCamera != null;
        }

        private Transform ResolveIntroShadowRoot()
        {
            if (introShadowRoot != null)
            {
                return introShadowRoot;
            }

            if (targetMeshDeformer != null)
            {
                ShadowMeshRootController rootController = targetMeshDeformer.GetComponentInParent<ShadowMeshRootController>();
                if (rootController != null)
                {
                    introShadowRoot = rootController.transform;
                    return introShadowRoot;
                }
            }

            ShadowMeshRootController foundRoot = FindObjectOfType<ShadowMeshRootController>();
            if (foundRoot != null)
            {
                introShadowRoot = foundRoot.transform;
            }

            return introShadowRoot;
        }

        private void SetInteractionSystemsEnabled(bool isEnabled)
        {
            if (deformationInput != null)
            {
                deformationInput.enabled = isEnabled;
            }

            if (interactionVisualizer != null)
            {
                if (!isEnabled)
                {
                    interactionVisualizer.HideRuntimeVisuals();
                }

                interactionVisualizer.enabled = isEnabled;
            }

            if (mediaPipeReceiver != null)
            {
                if (!isEnabled)
                {
                    mediaPipeReceiver.StopReceiver();
                }

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

        private void ResetInteractionProgress()
        {
            LastMatchScore = 0.0f;
            matchedSeconds = 0.0f;
            lastEvaluationTime = 0.0f;
            nextEvaluationTime = Time.unscaledTime + Mathf.Max(0.02f, evaluationIntervalSeconds);
            UpdateMatchProgressBar(0.0f);
        }

        private void SetIntroObjectsVisible(bool isVisible)
        {
            if (introObjects == null)
            {
                return;
            }

            for (int i = 0; i < introObjects.Length; i++)
            {
                if (introObjects[i] != null)
                {
                    introObjects[i].SetActive(isVisible);
                }
            }
        }

        private void StartIntroReveal()
        {
            StopIntroReveal();
            introRevealRoutine = StartCoroutine(PlayIntroRevealRoutine());
        }

        private void StopIntroReveal()
        {
            if (introRevealRoutine == null)
            {
                StopIntroNarrationPlayback();
                return;
            }

            StopCoroutine(introRevealRoutine);
            introRevealRoutine = null;
            StopIntroNarrationPlayback();
        }

        private IEnumerator PlayIntroRevealRoutine()
        {
            ResolveRuntimeReferences();
            Transform shadowRoot = ResolveIntroShadowRoot();
            CaptureIntroShadowRootRestPose(shadowRoot);
            Vector3 startPosition = shadowRoot != null ? shadowRoot.localPosition : Vector3.zero;
            Vector3 startScale = shadowRoot != null ? shadowRoot.localScale : Vector3.one;
            Quaternion startRotation = shadowRoot != null ? shadowRoot.localRotation : Quaternion.identity;
            Vector3 targetPosition = new Vector3(
                introShadowRootTargetPosition.x,
                introShadowRootTargetPosition.y,
                startPosition.z);
            Vector3 targetScale = ScalePreservingAspect(startScale, introShadowRootTargetScale);
            Quaternion targetRotation = startRotation * Quaternion.Euler(0.0f, 180.0f, 0.0f);
            float duration = Mathf.Max(0.0f, introRevealSeconds);

            if (duration <= 0.0f)
            {
                SetIntroBackgroundAlpha(IntroBackgroundFinalAlpha01);
                if (shadowRoot != null)
                {
                    shadowRoot.localPosition = targetPosition;
                    shadowRoot.localScale = targetScale;
                    shadowRoot.localRotation = targetRotation;
                    centeredMeshInMission = false;
                }

                yield return PlayIntroNarrationAndScrollRoutine();
                yield return PlayIntroToInteractionTransitionRoutine();
                yield return PlayPreInteractionTutorialRoutine();
                introRevealRoutine = null;
                EnterInteraction();
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                SetIntroBackgroundAlpha(eased * IntroBackgroundFinalAlpha01);

                if (shadowRoot != null)
                {
                    shadowRoot.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                    shadowRoot.localScale = Vector3.LerpUnclamped(startScale, targetScale, eased);
                    shadowRoot.localRotation = Quaternion.SlerpUnclamped(startRotation, targetRotation, eased);
                }

                yield return null;
            }

            SetIntroBackgroundAlpha(IntroBackgroundFinalAlpha01);
            if (shadowRoot != null)
            {
                shadowRoot.localPosition = targetPosition;
                shadowRoot.localScale = targetScale;
                shadowRoot.localRotation = targetRotation;
                centeredMeshInMission = false;
            }

            yield return PlayIntroNarrationAndScrollRoutine();
            yield return PlayIntroToInteractionTransitionRoutine();
            yield return PlayPreInteractionTutorialRoutine();
            introRevealRoutine = null;
            EnterInteraction();
        }

        private float IntroBackgroundFinalAlpha01 => Mathf.Clamp(introBackgroundFinalAlpha, 0, 255) / 255.0f;

        private static Vector3 ScalePreservingAspect(Vector3 referenceScale, float scaleMultiplier)
        {
            scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
            float zScale = Mathf.Abs(referenceScale.z) > 0.0001f
                ? referenceScale.z * scaleMultiplier
                : scaleMultiplier;
            return new Vector3(
                referenceScale.x * scaleMultiplier,
                referenceScale.y * scaleMultiplier,
                zScale);
        }

        private IEnumerator PlayIntroNarrationAndScrollRoutine()
        {
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveIntroNarrationPlayer();
            if (narrationPlayer == null || narrationPlayer.StepCount == 0)
            {
                yield break;
            }

            Transform backgroundTransform = ResolveIntroBackgroundTransform();
            Vector3 startPosition = backgroundTransform != null ? backgroundTransform.localPosition : Vector3.zero;
            Vector3 targetPosition = new Vector3(introBackgroundScrollTargetX, startPosition.y, startPosition.z);
            float scrollDuration = narrationPlayer.CalculateDurationThroughStep(IntroBackgroundScrollStepCount - 1);
            int introStepCount = Mathf.Clamp(introNarrationStepCount, 0, narrationPlayer.StepCount);
            float scrollElapsed = 0.0f;
            Transform shadowRoot = ResolveIntroShadowRoot();
            Vector3 shadowWalkBasePosition = shadowRoot != null ? shadowRoot.position : Vector3.zero;
            Quaternion shadowWalkBaseRotation = shadowRoot != null ? shadowRoot.localRotation : Quaternion.identity;
            float shadowWalkElapsed = 0.0f;
            bool backgroundScrolls = scrollDuration > 0.0f &&
                                     backgroundTransform != null &&
                                     !Mathf.Approximately(startPosition.x, targetPosition.x);
            float shadowWalkDirection = backgroundScrolls ? -Mathf.Sign(targetPosition.x - startPosition.x) : 1.0f;
            bool darkFadeReset = false;
            bool starRevealStarted = false;

            yield return narrationPlayer.PlayRangeAndWaitRoutine(
                0,
                introStepCount,
                (stepIndex, clipElapsed, clipDuration, deltaTime) =>
                {
                    AdvanceIntroBackgroundScroll(
                        backgroundTransform,
                        startPosition,
                        targetPosition,
                        scrollDuration,
                        deltaTime,
                        elapsed => scrollElapsed = elapsed,
                        () => scrollElapsed);
                    if (backgroundScrolls && shadowRoot != null && shadowWalkElapsed < scrollDuration)
                    {
                        shadowWalkElapsed = Mathf.Min(scrollDuration, shadowWalkElapsed + Mathf.Max(0.0f, deltaTime));
                        StarWalkMotion.ApplyWorldInPlace(
                            shadowRoot,
                            shadowWalkBasePosition,
                            shadowWalkElapsed,
                            shadowWalkDirection,
                            shadowWalkBaseRotation);
                    }

                    if (stepIndex == IntroDarkFadeStepIndex)
                    {
                        if (!darkFadeReset)
                        {
                            SetIntroDarkAlpha(0.0f);
                            darkFadeReset = true;
                        }

                        float darkT = clipDuration > 0.0f ? Mathf.Clamp01(clipElapsed / clipDuration) : 1.0f;
                        SetIntroDarkAlpha(darkT * IntroDarkFinalAlpha01);
                    }

                    if (stepIndex == introStarRevealNarrationStepIndex)
                    {
                        if (!starRevealStarted)
                        {
                            SetIntroStarAlpha(0.0f);
                            starRevealStarted = true;
                        }

                        float starT = Mathf.Clamp01(clipElapsed / Mathf.Max(0.01f, introStarRevealSeconds));
                        SetIntroStarAlpha(Mathf.SmoothStep(0.0f, IntroStarFinalAlpha01, starT));
                    }
                },
                (stepIndex, gapElapsed, gapDuration, deltaTime) =>
                {
                    AdvanceIntroBackgroundScroll(
                        backgroundTransform,
                        startPosition,
                        targetPosition,
                        scrollDuration,
                        deltaTime,
                        elapsed => scrollElapsed = elapsed,
                        () => scrollElapsed);
                    if (backgroundScrolls && shadowRoot != null && shadowWalkElapsed < scrollDuration)
                    {
                        shadowWalkElapsed = Mathf.Min(scrollDuration, shadowWalkElapsed + Mathf.Max(0.0f, deltaTime));
                        StarWalkMotion.ApplyWorldInPlace(
                            shadowRoot,
                            shadowWalkBasePosition,
                            shadowWalkElapsed,
                            shadowWalkDirection,
                            shadowWalkBaseRotation);
                    }
                });

            if (darkFadeReset)
            {
                SetIntroDarkAlpha(IntroDarkFinalAlpha01);
            }

            if (starRevealStarted)
            {
                SetIntroStarAlpha(IntroStarFinalAlpha01);
            }

            if (scrollDuration > 0.0f && backgroundTransform != null)
            {
                backgroundTransform.localPosition = targetPosition;
            }

            if (backgroundScrolls && shadowRoot != null)
            {
                StarWalkMotion.FinishWorld(shadowRoot, shadowWalkBasePosition, shadowWalkBaseRotation);
            }
        }

        private static void AdvanceIntroBackgroundScroll(
            Transform backgroundTransform,
            Vector3 backgroundStartPosition,
            Vector3 backgroundTargetPosition,
            float scrollDuration,
            float deltaTime,
            Action<float> setScrollElapsed,
            Func<float> getScrollElapsed)
        {
            if (scrollDuration <= 0.0f || backgroundTransform == null || getScrollElapsed == null || setScrollElapsed == null)
            {
                return;
            }

            float scrollElapsed = getScrollElapsed();
            if (scrollElapsed >= scrollDuration)
            {
                return;
            }

            scrollElapsed = Mathf.Min(scrollDuration, scrollElapsed + Mathf.Max(0.0f, deltaTime));
            setScrollElapsed(scrollElapsed);
            UpdateIntroBackgroundScroll(backgroundTransform, backgroundStartPosition, backgroundTargetPosition, scrollElapsed, scrollDuration);
        }

        private Transform ResolveIntroBackgroundTransform()
        {
            return introBackgroundTransform;
        }

        private Renderer ResolveIntroDarkRenderer()
        {
            return introDarkRenderer;
        }

        private NarrationSubtitleSequencePlayer ResolveIntroNarrationPlayer()
        {
            if (introNarrationPlayer != null)
            {
                return introNarrationPlayer;
            }

            introNarrationPlayer = GetComponent<NarrationSubtitleSequencePlayer>();
            return introNarrationPlayer;
        }

        private void StopIntroNarrationPlayback()
        {
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveIntroNarrationPlayer();
            if (narrationPlayer != null)
            {
                narrationPlayer.StopPlayback();
            }
        }

        private void CaptureIntroShadowRootRestPose(Transform shadowRoot)
        {
            if (shadowRoot == null || hasIntroShadowRootRestPose)
            {
                return;
            }

            introShadowRootRestLocalScale = shadowRoot.localScale;
            introShadowRootRestLocalRotation = shadowRoot.localRotation;
            hasIntroShadowRootRestPose = true;
        }

        private void CaptureIntroStarRestPose(Transform starTransform)
        {
            if (starTransform == null || hasIntroStarRestPose)
            {
                return;
            }

            introStarRestLocalPosition = starTransform.localPosition;
            introStarRestLocalRotation = starTransform.localRotation;
            introStarRestLocalScale = starTransform.localScale;
            hasIntroStarRestPose = true;
        }

        private IEnumerator PlayIntroToInteractionTransitionRoutine()
        {
            ResolveRuntimeReferences();
            Transform backgroundTransform = ResolveIntroBackgroundTransform();
            Transform shadowRoot = ResolveIntroShadowRoot();
            Transform starTransform = ResolveIntroStarTransform();
            CaptureIntroStarRestPose(starTransform);

            if (createGuideOverlay)
            {
                RebuildGuidePolygonIfNeeded(force: true);
                if (guideLineObject == null)
                {
                    CreateGuideLine();
                }

                SetGuideVisible(true);
                UpdateGuideLine();
            }

            float duration = Mathf.Max(0.0f, introToInteractionTransitionSeconds);
            Vector3 shadowStartPosition = shadowRoot != null ? shadowRoot.localPosition : Vector3.zero;
            Vector3 shadowStartScale = shadowRoot != null ? shadowRoot.localScale : Vector3.one;
            Quaternion shadowStartRotation = shadowRoot != null ? shadowRoot.localRotation : Quaternion.identity;
            Vector3 shadowTargetScale = hasIntroShadowRootRestPose ? introShadowRootRestLocalScale : Vector3.one;
            Quaternion shadowTargetRotation = hasIntroShadowRootRestPose ? introShadowRootRestLocalRotation : Quaternion.identity;
            Vector3 shadowTargetPosition = ResolveCenteredShadowRootLocalPosition(shadowRoot, shadowTargetScale, shadowStartPosition);

            Vector3 starStartPosition = starTransform != null ? starTransform.localPosition : Vector3.zero;
            Vector3 starStartScale = starTransform != null ? starTransform.localScale : Vector3.one;
            Vector3 starTargetPosition = ResolveCameraCenterLocalPosition(starTransform, starStartPosition);
            Vector3 starTargetScale = Vector3.one * Mathf.Max(0.01f, introStarInteractionScale);

            if (duration <= 0.0f)
            {
                SetIntroBackgroundAlpha(0.0f);
                SetIntroDarkAlpha(0.0f);
                ApplyIntroTransitionFinalTransforms(shadowRoot, shadowTargetPosition, shadowTargetScale, shadowTargetRotation, starTransform, starTargetPosition, starTargetScale);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);

                SetIntroBackgroundAlpha(Mathf.LerpUnclamped(IntroBackgroundFinalAlpha01, 0.0f, eased));
                SetIntroDarkAlpha(Mathf.LerpUnclamped(IntroDarkFinalAlpha01, 0.0f, eased));

                if (shadowRoot != null)
                {
                    shadowRoot.localPosition = Vector3.LerpUnclamped(shadowStartPosition, shadowTargetPosition, eased);
                    shadowRoot.localScale = Vector3.LerpUnclamped(shadowStartScale, shadowTargetScale, eased);
                    shadowRoot.localRotation = Quaternion.SlerpUnclamped(shadowStartRotation, shadowTargetRotation, eased);
                }

                if (starTransform != null)
                {
                    starTransform.localPosition = Vector3.LerpUnclamped(starStartPosition, starTargetPosition, eased);
                    starTransform.localScale = Vector3.LerpUnclamped(starStartScale, starTargetScale, eased);
                    SetIntroStarAlpha(IntroStarFinalAlpha01);
                }

                UpdateGuideLine();
                yield return null;
            }

            SetIntroBackgroundAlpha(0.0f);
            SetIntroDarkAlpha(0.0f);
            ApplyIntroTransitionFinalTransforms(shadowRoot, shadowTargetPosition, shadowTargetScale, shadowTargetRotation, starTransform, starTargetPosition, starTargetScale);
        }

        private void ApplyIntroTransitionFinalTransforms(
            Transform shadowRoot,
            Vector3 shadowTargetPosition,
            Vector3 shadowTargetScale,
            Quaternion shadowTargetRotation,
            Transform starTransform,
            Vector3 starTargetPosition,
            Vector3 starTargetScale)
        {
            if (shadowRoot != null)
            {
                shadowRoot.localPosition = shadowTargetPosition;
                shadowRoot.localScale = shadowTargetScale;
                shadowRoot.localRotation = shadowTargetRotation;
                centeredMeshInMission = true;
            }

            if (starTransform != null)
            {
                starTransform.localPosition = starTargetPosition;
                starTransform.localScale = starTargetScale;
                SetIntroStarAlpha(IntroStarFinalAlpha01);
            }

            UpdateGuideLine();
        }

        private Transform ResolveIntroStarTransform()
        {
            Renderer[] renderers = ResolveIntroStarRenderers();
            if (renderers.Length > 0 && renderers[0] != null)
            {
                return renderers[0].transform;
            }

            return null;
        }

        private Vector3 ResolveCenteredShadowRootLocalPosition(Transform shadowRoot, Vector3 targetScale, Vector3 defaultPosition)
        {
            if (shadowRoot == null || targetMeshDeformer == null || targetCamera == null)
            {
                return defaultPosition;
            }

            ShadowMeshRootController rootController = shadowRoot.GetComponent<ShadowMeshRootController>();
            if (rootController == null)
            {
                rootController = shadowRoot.GetComponentInParent<ShadowMeshRootController>();
            }

            if (rootController == null)
            {
                return defaultPosition;
            }

            Vector3 originalPosition = shadowRoot.localPosition;
            Vector3 originalScale = shadowRoot.localScale;
            shadowRoot.localScale = targetScale;
            rootController.CenterMeshInCamera(targetMeshDeformer, targetCamera);
            Vector3 centeredPosition = shadowRoot.localPosition;
            shadowRoot.localPosition = originalPosition;
            shadowRoot.localScale = originalScale;
            return centeredPosition;
        }

        private Vector3 ResolveCameraCenterLocalPosition(Transform targetTransform, Vector3 defaultPosition)
        {
            if (targetTransform == null || targetCamera == null)
            {
                return defaultPosition;
            }

            float planeDistance = Mathf.Abs(Vector3.Dot(
                targetTransform.position - targetCamera.transform.position,
                targetCamera.transform.forward));
            planeDistance = Mathf.Max(targetCamera.nearClipPlane, planeDistance);
            Vector3 worldCenter = targetCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, planeDistance));
            Vector3 localCenter = targetTransform.parent == null
                ? worldCenter
                : targetTransform.parent.InverseTransformPoint(worldCenter);

            return new Vector3(localCenter.x, localCenter.y, defaultPosition.z);
        }

        private static void UpdateIntroBackgroundScroll(
            Transform backgroundTransform,
            Vector3 startPosition,
            Vector3 targetPosition,
            float elapsed,
            float totalDuration)
        {
            if (backgroundTransform == null)
            {
                return;
            }

            float t = totalDuration > 0.0f ? Mathf.Clamp01(elapsed / totalDuration) : 1.0f;
            backgroundTransform.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, t);
        }

        private void MoveIntroBackgroundToScrollTarget(Transform backgroundTransform)
        {
            if (backgroundTransform == null)
            {
                return;
            }

            Vector3 position = backgroundTransform.localPosition;
            position.x = introBackgroundScrollTargetX;
            backgroundTransform.localPosition = position;
        }

        private float IntroDarkFinalAlpha01 => Mathf.Clamp(introDarkFinalAlpha, 0, 255) / 255.0f;

        private float IntroStarFinalAlpha01 => Mathf.Clamp(introStarFinalAlpha, 0, 255) / 255.0f;

        private void SetIntroDarkAlpha(float alpha)
        {
            Renderer darkRenderer = ResolveIntroDarkRenderer();
            if (darkRenderer == null)
            {
                return;
            }

            if (introPropertyBlock == null)
            {
                introPropertyBlock = new MaterialPropertyBlock();
            }

            alpha = Mathf.Clamp01(alpha);
            darkRenderer.GetPropertyBlock(introPropertyBlock);
            Color color = Color.black;
            color.a = alpha;
            introPropertyBlock.SetColor(BaseColorId, color);
            introPropertyBlock.SetColor(ColorId, color);
            darkRenderer.SetPropertyBlock(introPropertyBlock);
        }

        private Renderer[] ResolveIntroStarRenderers()
        {
            return introStarRenderers ?? Array.Empty<Renderer>();
        }

        private void SetIntroStarAlpha(float alpha)
        {
            Renderer[] renderers = ResolveIntroStarRenderers();
            if (renderers.Length == 0)
            {
                return;
            }

            if (introPropertyBlock == null)
            {
                introPropertyBlock = new MaterialPropertyBlock();
            }

            alpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < renderers.Length; i++)
            {
                ApplyIntroRendererAlpha(renderers[i], alpha);
                ConfigureIntroStarRendererMaterial(renderers[i]);
            }
        }

        private static void ConfigureIntroStarRendererMaterial(Renderer targetRenderer)
        {
            if (targetRenderer == null)
            {
                return;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                ConfigureTransparentOverlayMaterial(materials[i]);
            }
        }

        private void SetIntroBackgroundAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);

            if (introPropertyBlock == null)
            {
                introPropertyBlock = new MaterialPropertyBlock();
            }

            if (introFadeRenderers != null)
            {
                for (int i = 0; i < introFadeRenderers.Length; i++)
                {
                    ApplyIntroRendererAlpha(introFadeRenderers[i], alpha);
                }
            }

            if (introObjects == null)
            {
                return;
            }

            for (int i = 0; i < introObjects.Length; i++)
            {
                if (introObjects[i] == null)
                {
                    continue;
                }

                Renderer[] renderers = introObjects[i].GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    ApplyIntroRendererAlpha(renderers[rendererIndex], alpha);
                }
            }
        }

        private void ApplyIntroRendererAlpha(Renderer targetRenderer, float alpha)
        {
            if (targetRenderer == null)
            {
                return;
            }

            targetRenderer.GetPropertyBlock(introPropertyBlock);
            Color color = Color.white;
            color.a = alpha;
            introPropertyBlock.SetColor(BaseColorId, color);
            introPropertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(introPropertyBlock);
        }

        private void CenterMeshForMissionIfNeeded()
        {
            if (!centerMeshOnMissionStart || centeredMeshInMission)
            {
                return;
            }

            ShadowMeshRootController rootController = targetMeshDeformer.GetComponentInParent<ShadowMeshRootController>();
            if (rootController == null)
            {
                rootController = FindObjectOfType<ShadowMeshRootController>();
            }

            if (rootController == null)
            {
                return;
            }

            rootController.CenterMeshInCamera(targetMeshDeformer, targetCamera);
            centeredMeshInMission = true;
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
            Debug.Log($"Mission1Controller: match score {LastMatchScore:0.000} reached; completing Mission1.");

            StopMediaPipeTracking();
            PlayCompletionDing();
            SetInteractionInstructionVisible(false);
            HideMatchProgressBar();

            if (replaceMeshWithStarOnComplete)
            {
                yield return MorphCurrentMeshToGuideStarRoutine();
                ReplaceShadowRootWithShadowStar();
            }

            PlayCompletionSparkle();

            float completionWaitSeconds = Mathf.Max(completionEffectSeconds, particleBurstSeconds);
            if (completionWaitSeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(completionWaitSeconds);
            }

            EnterOutro();
            yield return PlayOutroReturnRoutine();

            float sceneDelay = Mathf.Max(0.0f, mission2SceneTransitionDelaySeconds);
            if (sceneDelay > 0.0f)
            {
                yield return new WaitForSecondsRealtime(sceneDelay);
            }

            PreserveIntroBackgroundThroughNextSceneFirstFrame();
            PreserveIntroStarThroughNextSceneFirstFrame(hideGuide: true);
            stateManager?.SetState(GameStateManager.PipelineState.Mission2);
            LoadNextScene();
        }

        private IEnumerator PlayOutroReturnRoutine()
        {
            GameObject shadowStar = ResolveShadowStarObject();
            Transform shadowStarTransform = shadowStar != null ? shadowStar.transform : null;
            CreateRecreatedIntroStarObject();

            SetIntroObjectsVisible(true);
            SetIntroBackgroundAlpha(0.0f);
            SetIntroDarkAlpha(0.0f);
            SetIntroStarAlpha(0.0f);

            Vector3 shadowStarStartPosition = shadowStarTransform != null ? shadowStarTransform.localPosition : Vector3.zero;
            Vector3 shadowStarStartScale = shadowStarTransform != null ? shadowStarTransform.localScale : Vector3.one;
            Vector3 shadowStarTargetPosition = new Vector3(
                outroShadowStarTargetPosition.x,
                outroShadowStarTargetPosition.y,
                shadowStarStartPosition.z);
            Vector3 shadowStarTargetScale = Vector3.one * Mathf.Max(0.01f, outroShadowStarTargetScale);
            float duration = Mathf.Max(0.0f, outroReturnSeconds);

            if (duration <= 0.0f)
            {
                ApplyOutroReturnFrame(shadowStarTransform, shadowStarTargetPosition, shadowStarTargetScale, 1.0f);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);

                ApplyOutroReturnFrame(
                    shadowStarTransform,
                    Vector3.LerpUnclamped(shadowStarStartPosition, shadowStarTargetPosition, eased),
                    Vector3.LerpUnclamped(shadowStarStartScale, shadowStarTargetScale, eased),
                    eased);

                yield return null;
            }

            ApplyOutroReturnFrame(shadowStarTransform, shadowStarTargetPosition, shadowStarTargetScale, 1.0f);
        }

        private void ApplyOutroReturnFrame(
            Transform shadowStarTransform,
            Vector3 shadowPosition,
            Vector3 shadowScale,
            float visibility)
        {
            float alpha = Mathf.Clamp01(visibility);
            SetIntroBackgroundAlpha(alpha * IntroBackgroundFinalAlpha01);
            SetIntroDarkAlpha(0.0f);
            SetIntroStarAlpha(alpha * IntroStarFinalAlpha01);

            if (shadowStarTransform != null)
            {
                shadowStarTransform.localPosition = shadowPosition;
                shadowStarTransform.localScale = shadowScale;
            }

            if (recreatedIntroStarObject != null)
            {
                Transform starTransform = recreatedIntroStarObject.transform;
                starTransform.localPosition = introStarRestLocalPosition;
                starTransform.localRotation = introStarRestLocalRotation;
                starTransform.localScale = introStarRestLocalScale;
            }
        }

        private void PreserveIntroBackgroundThroughNextSceneFirstFrame()
        {
            Transform backgroundTransform = ResolveIntroBackgroundTransform();
            if (backgroundTransform == null)
            {
                return;
            }

            GameObject backgroundObject = backgroundTransform.gameObject;
            backgroundObject.name = "Mission1TransitionBackground";
            backgroundObject.SetActive(true);
            backgroundTransform.SetParent(null, true);
            SetIntroBackgroundAlpha(IntroBackgroundFinalAlpha01);

            DontDestroyOnLoad(backgroundObject);

            SceneLoadDeferredDestroyer destroyer =
                backgroundObject.GetComponent<SceneLoadDeferredDestroyer>();
            if (destroyer == null)
            {
                destroyer = backgroundObject.AddComponent<SceneLoadDeferredDestroyer>();
            }

            destroyer.DestroyAfterNextSceneLoad(SceneTransitionBackgroundDestroyFrames);
        }

        private void PreserveIntroStarThroughNextSceneFirstFrame(bool hideGuide)
        {
            if (hideGuide)
            {
                SetGuideVisible(false);
            }

            Transform starTransform = ResolveIntroStarTransform();
            if (starTransform == null)
            {
                introStarRenderers = Array.Empty<Renderer>();
                return;
            }

            GameObject starObject = starTransform.gameObject;
            starObject.name = "Mission1TransitionStar";
            starObject.SetActive(true);
            starTransform.SetParent(null, true);
            SetIntroStarAlpha(IntroStarFinalAlpha01);

            DontDestroyOnLoad(starObject);

            SceneLoadDeferredDestroyer destroyer =
                starObject.GetComponent<SceneLoadDeferredDestroyer>();
            if (destroyer == null)
            {
                destroyer = starObject.AddComponent<SceneLoadDeferredDestroyer>();
            }

            if (starObject == recreatedIntroStarObject)
            {
                destroyer.RegisterRuntimeObject(recreatedIntroStarMesh);
                destroyer.RegisterRuntimeObject(recreatedIntroStarMaterial);
                recreatedIntroStarObject = null;
                recreatedIntroStarMesh = null;
                recreatedIntroStarMaterial = null;
            }

            introStarRenderers = Array.Empty<Renderer>();
            destroyer.DestroyAfterNextSceneLoad(SceneTransitionBackgroundDestroyFrames);
        }

        private void LoadNextScene()
        {
            if (sceneFlowController != null)
            {
                sceneFlowController.LoadScene(nextSceneName);
                return;
            }

            if (!string.IsNullOrWhiteSpace(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }

        private void HideMatchProgressBar()
        {
            SetMatchProgressBarVisible(false);
        }

        private void SetMatchProgressBarVisible(bool isVisible)
        {
            RectTransform barRoot = null;
            if (matchProgressFillRect != null)
            {
                barRoot = matchProgressFillRect.parent as RectTransform;
            }

            if (barRoot == null && matchThresholdMarker != null)
            {
                barRoot = matchThresholdMarker.parent as RectTransform;
            }

            if (barRoot != null)
            {
                barRoot.gameObject.SetActive(isVisible);
                return;
            }

            matchProgressFillRect?.gameObject.SetActive(isVisible);
            matchThresholdMarker?.gameObject.SetActive(isVisible);
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

        private IEnumerator PlayPreInteractionTutorialRoutine()
        {
            if (preInteractionTutorialObject == null || preInteractionTutorialSeconds <= 0.0f)
            {
                SetPreInteractionTutorialVisible(false);
                yield break;
            }

            SetPreInteractionTutorialVisible(true);
            yield return new WaitForSecondsRealtime(preInteractionTutorialSeconds);
            SetPreInteractionTutorialVisible(false);
        }

        private void SetPreInteractionTutorialVisible(bool isVisible)
        {
            if (preInteractionTutorialObject != null)
            {
                preInteractionTutorialObject.SetActive(isVisible);
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
                rectTransform.anchorMin = new Vector2(0.5f, 1.0f);
                rectTransform.anchorMax = new Vector2(0.5f, 1.0f);
                rectTransform.pivot = new Vector2(0.5f, 1.0f);
                rectTransform.anchoredPosition = new Vector2(0.0f, -Mathf.Max(0.0f, interactionInstructionTopMargin));
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

        private void SetGuideVisible(bool isVisible)
        {
            if (guideLineObject != null)
            {
                guideLineObject.SetActive(isVisible);
            }
        }

        private void ReplaceShadowRootWithShadowStar()
        {
            DestroyMission1ShadowMeshRoot();
            SetShadowStarVisible(true);
        }

        private void SetShadowStarVisible(bool isVisible)
        {
            GameObject resolvedShadowStar = ResolveShadowStarObject();
            if (resolvedShadowStar != null)
            {
                resolvedShadowStar.SetActive(isVisible);
                Renderer[] renderers = resolvedShadowStar.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        renderers[i].enabled = isVisible;
                    }
                }
            }
        }

        private GameObject ResolveShadowStarObject()
        {
            return shadowStarObject;
        }

        private void DestroyMission1ShadowMeshRoot()
        {
            var rootsToDestroy = new HashSet<GameObject>();
            if (targetMeshDeformer != null)
            {
                ShadowMeshRootController targetRoot = targetMeshDeformer.GetComponentInParent<ShadowMeshRootController>();
                if (targetRoot != null)
                {
                    rootsToDestroy.Add(targetRoot.gameObject);
                }
            }

            ShadowMeshRootController[] shadowRoots = UnityEngine.Object.FindObjectsOfType<ShadowMeshRootController>();
            for (int i = 0; i < shadowRoots.Length; i++)
            {
                if (shadowRoots[i] != null)
                {
                    rootsToDestroy.Add(shadowRoots[i].gameObject);
                }
            }

            foreach (GameObject root in rootsToDestroy)
            {
                if (root != null)
                {
                    Destroy(root);
                }
            }

            targetMeshDeformer = null;
            introShadowRoot = null;
        }

        private void DestroyIntroStarObject(bool hideGuide)
        {
            if (hideGuide)
            {
                SetGuideVisible(false);
            }

            Transform starTransform = ResolveIntroStarTransform();
            introStarRenderers = Array.Empty<Renderer>();
            if (starTransform == null)
            {
                return;
            }

            if (starTransform.gameObject == recreatedIntroStarObject)
            {
                DestroyRecreatedIntroStarObject();
                return;
            }

            starTransform.gameObject.SetActive(false);
            Destroy(starTransform.gameObject);
        }

        private Transform CreateRecreatedIntroStarObject()
        {
            DestroyRecreatedIntroStarObject();

            recreatedIntroStarObject = new GameObject("Star");
            Transform starTransform = recreatedIntroStarObject.transform;
            starTransform.localPosition = introStarRestLocalPosition;
            starTransform.localRotation = introStarRestLocalRotation;
            starTransform.localScale = introStarRestLocalScale;

            MeshFilter meshFilter = recreatedIntroStarObject.AddComponent<MeshFilter>();
            recreatedIntroStarMesh = BuildStandaloneStarMesh(1.0f, 0.43f);
            meshFilter.sharedMesh = recreatedIntroStarMesh;

            MeshRenderer meshRenderer = recreatedIntroStarObject.AddComponent<MeshRenderer>();
            recreatedIntroStarMaterial = CreateUnlitMaterial(Color.white);
            recreatedIntroStarMaterial.name = "Mission1OutroStar_Runtime";
            meshRenderer.sharedMaterial = recreatedIntroStarMaterial;
            meshRenderer.sortingOrder = 15;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            introStarRenderers = new[] { meshRenderer };
            SetIntroStarAlpha(0.0f);
            return starTransform;
        }

        private void DestroyRecreatedIntroStarObject()
        {
            if (recreatedIntroStarObject != null)
            {
                recreatedIntroStarObject.SetActive(false);
                Destroy(recreatedIntroStarObject);
                recreatedIntroStarObject = null;
            }

            DestroyRuntimeObject(recreatedIntroStarMesh);
            DestroyRuntimeObject(recreatedIntroStarMaterial);
            recreatedIntroStarMesh = null;
            recreatedIntroStarMaterial = null;
        }

        private static void StopMediaPipeTracking()
        {
            MediaPipeInteractionVisualizer[] visualizers = UnityEngine.Object.FindObjectsOfType<MediaPipeInteractionVisualizer>();
            for (int i = 0; i < visualizers.Length; i++)
            {
                visualizers[i].HideRuntimeVisuals();
                visualizers[i].enabled = false;
            }

            MediaPipeMeshDeformationInput[] deformationInputs = UnityEngine.Object.FindObjectsOfType<MediaPipeMeshDeformationInput>();
            for (int i = 0; i < deformationInputs.Length; i++)
            {
                deformationInputs[i].enabled = false;
            }

            MediaPipeUdpReceiver[] receivers = UnityEngine.Object.FindObjectsOfType<MediaPipeUdpReceiver>();
            for (int i = 0; i < receivers.Length; i++)
            {
                receivers[i].StopReceiver();
                receivers[i].enabled = false;
            }

            MediaPipeTrackingProcessLauncher[] launchers = UnityEngine.Object.FindObjectsOfType<MediaPipeTrackingProcessLauncher>();
            for (int i = 0; i < launchers.Length; i++)
            {
                launchers[i].enabled = false;
            }
        }

        private static Mesh BuildStandaloneStarMesh(float outerRadius, float innerRadius)
        {
            Vector3[] vertices = new Vector3[StarPointCount + 1];
            int[] triangles = new int[StarPointCount * 3];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < StarPointCount; i++)
            {
                bool outerPoint = i % 2 == 0;
                float radius = outerPoint ? outerRadius : innerRadius;
                float angle = (Mathf.PI * 0.5f) + (i * Mathf.PI / 5.0f);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.0f);

                int triangleIndex = i * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = i + 1;
                triangles[triangleIndex + 2] = ((i + 1) % StarPointCount) + 1;
            }

            var mesh = new Mesh
            {
                name = "Mission1OutroStar_Runtime",
                hideFlags = HideFlags.DontSave
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private float ResolveMeshPlaneDistance()
        {
            if (targetCamera == null)
            {
                return 1.0f;
            }

            Vector3 meshCenter = targetMeshDeformer != null && targetMeshDeformer.HasMesh
                ? targetMeshDeformer.GetWorldBounds().center
                : Vector3.zero;
            float distance = Mathf.Abs(Vector3.Dot(
                meshCenter - targetCamera.transform.position,
                targetCamera.transform.forward));
            return Mathf.Max(targetCamera.nearClipPlane, distance);
        }

        private IEnumerator MorphCurrentMeshToGuideStarRoutine()
        {
            if (starMorphSeconds <= 0.0f ||
                targetMeshDeformer == null ||
                targetCamera == null ||
                !targetMeshDeformer.HasMesh ||
                guidePolygon.Length != StarPointCount ||
                !targetMeshDeformer.TryGetCurrentLocalTriangles(out Vector2[] currentVertices2D, out _))
            {
                yield break;
            }

            if (!TryBuildGuideStarLocalShape(out Vector2 starCenterLocal, out Vector2[] starPointsLocal))
            {
                yield break;
            }

            Vector3[] sourceVertices = new Vector3[currentVertices2D.Length];
            for (int i = 0; i < currentVertices2D.Length; i++)
            {
                sourceVertices[i] = new Vector3(currentVertices2D[i].x, currentVertices2D[i].y, 0.0f);
            }

            bool[] boundaryVertexMask = BuildBoundaryVertexMask(sourceVertices.Length);
            MorphVertexTarget[] morphTargets = BuildStarMorphTargets(
                sourceVertices,
                starCenterLocal,
                starPointsLocal,
                boundaryVertexMask);
            if (morphTargets.Length != sourceVertices.Length)
            {
                yield break;
            }

            Vector3[] frameVertices = new Vector3[sourceVertices.Length];
            float elapsed = 0.0f;
            while (elapsed < starMorphSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, starMorphSeconds));
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    MorphVertexTarget target = morphTargets[i];
                    float localTime = Mathf.SmoothStep(
                        0.0f,
                        1.0f,
                        Mathf.InverseLerp(target.Delay, 1.0f, normalizedTime));
                    Vector3 position = Vector3.LerpUnclamped(sourceVertices[i], target.TargetLocal, localTime);
                    float wobble = Mathf.Sin((normalizedTime * Mathf.PI * 5.0f) + target.Phase) *
                        Mathf.Sin(localTime * Mathf.PI) *
                        target.WobbleAmplitude;
                    position += new Vector3(target.WobbleDirection.x, target.WobbleDirection.y, 0.0f) * wobble;
                    frameVertices[i] = position;
                }

                targetMeshDeformer.SetRuntimeMeshVertices(frameVertices);
                yield return null;
            }

            for (int i = 0; i < sourceVertices.Length; i++)
            {
                frameVertices[i] = morphTargets[i].TargetLocal;
            }

            targetMeshDeformer.SetRuntimeMeshVertices(frameVertices, forceColliderRefresh: true);
        }

        private MorphVertexTarget[] BuildStarMorphTargets(
            Vector3[] sourceVertices,
            Vector2 starCenterLocal,
            Vector2[] starPointsLocal,
            bool[] boundaryVertexMask)
        {
            MorphVertexTarget[] targets = new MorphVertexTarget[sourceVertices.Length];
            Vector2 sourceCenter = ComputeSourceBoundsCenter(sourceVertices);

            float starRadius = 0.0001f;
            for (int i = 0; i < starPointsLocal.Length; i++)
            {
                starRadius = Mathf.Max(starRadius, Vector2.Distance(starPointsLocal[i], starCenterLocal));
            }

            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector2 source = new Vector2(sourceVertices[i].x, sourceVertices[i].y);
                Vector2 fromCenter = source - sourceCenter;
                float sourceRadius = fromCenter.magnitude;
                float defaultAngle = i * 2.399963f;
                Vector2 direction = sourceRadius > 0.0001f
                    ? fromCenter / sourceRadius
                    : new Vector2(Mathf.Cos(defaultAngle), Mathf.Sin(defaultAngle));

                Vector2 starBoundary = ResolveStarBoundaryPoint(starCenterLocal, starPointsLocal, direction);
                float sourceBoundaryRadius = EstimateSourceBoundaryRadius(sourceVertices, sourceCenter, direction);
                float radius01 = Mathf.Clamp01(sourceRadius / sourceBoundaryRadius);
                bool isBoundaryVertex = boundaryVertexMask != null &&
                    i >= 0 &&
                    i < boundaryVertexMask.Length &&
                    boundaryVertexMask[i];
                Vector2 sourceBoundary = sourceCenter + (direction * sourceBoundaryRadius);
                Vector2 boundaryDelta = starBoundary - sourceBoundary;
                float shellFactor = isBoundaryVertex
                    ? 1.0f
                    : Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(0.45f, 1.0f, radius01));
                Vector2 target2D = isBoundaryVertex
                    ? starBoundary
                    : source + (boundaryDelta * shellFactor);
                Vector2 wobbleDirection = new Vector2(-direction.y, direction.x);
                float jitter = Hash01(i) * 0.035f;

                targets[i] = new MorphVertexTarget
                {
                    TargetLocal = new Vector3(target2D.x, target2D.y, sourceVertices[i].z),
                    Delay = jitter,
                    Phase = Hash01(i + 137) * Mathf.PI * 2.0f,
                    WobbleDirection = wobbleDirection,
                    WobbleAmplitude = starRadius * shellFactor * Mathf.Lerp(0.004f, 0.018f, Hash01(i + 271))
                };
            }

            return targets;
        }

        private bool[] BuildBoundaryVertexMask(int vertexCount)
        {
            bool[] mask = new bool[Mathf.Max(0, vertexCount)];
            int[] boundaryIndices = targetMeshDeformer != null ? targetMeshDeformer.BoundaryIndices : null;
            if (boundaryIndices == null || boundaryIndices.Length == 0)
            {
                return mask;
            }

            for (int i = 0; i < boundaryIndices.Length; i++)
            {
                int vertexIndex = boundaryIndices[i];
                if (vertexIndex < 0 || vertexIndex >= mask.Length)
                {
                    continue;
                }

                mask[vertexIndex] = true;
            }

            return mask;
        }

        private static Vector2 ComputeSourceBoundsCenter(Vector3[] sourceVertices)
        {
            if (sourceVertices == null || sourceVertices.Length == 0)
            {
                return Vector2.zero;
            }

            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector2 point = new Vector2(sourceVertices[i].x, sourceVertices[i].y);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return (min + max) * 0.5f;
        }

        private static float EstimateSourceBoundaryRadius(Vector3[] sourceVertices, Vector2 sourceCenter, Vector2 direction)
        {
            float bestProjection = 0.0001f;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector2 point = new Vector2(sourceVertices[i].x, sourceVertices[i].y);
                float projection = Vector2.Dot(point - sourceCenter, direction);
                if (projection > bestProjection)
                {
                    bestProjection = projection;
                }
            }

            return bestProjection;
        }

        private bool TryBuildGuideStarLocalShape(out Vector2 centerLocal, out Vector2[] pointsLocal)
        {
            centerLocal = Vector2.zero;
            pointsLocal = Array.Empty<Vector2>();

            if (targetCamera == null || targetMeshDeformer == null || guidePolygon.Length != StarPointCount)
            {
                return false;
            }

            float meshPlaneDistance = ResolveMeshPlaneDistance();
            centerLocal = ViewportToMeshLocal(new Vector2(0.5f, 0.5f), meshPlaneDistance);
            pointsLocal = new Vector2[guidePolygon.Length];
            for (int i = 0; i < guidePolygon.Length; i++)
            {
                pointsLocal[i] = ViewportToMeshLocal(guidePolygon[i], meshPlaneDistance);
            }

            return true;
        }

        private Vector2 ResolveStarBoundaryPoint(Vector2 center, Vector2[] starPoints, Vector2 direction)
        {
            float bestDistance = float.PositiveInfinity;
            Vector2 bestPoint = center;
            for (int i = 0; i < starPoints.Length; i++)
            {
                Vector2 a = starPoints[i];
                Vector2 b = starPoints[(i + 1) % starPoints.Length];
                if (!TryRaySegmentIntersection(center, direction, a, b, out float distance, out Vector2 point) ||
                    distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestPoint = point;
            }

            if (!float.IsPositiveInfinity(bestDistance))
            {
                return bestPoint;
            }

            int nearestIndex = 0;
            float bestAlignment = float.NegativeInfinity;
            for (int i = 0; i < starPoints.Length; i++)
            {
                Vector2 toPoint = starPoints[i] - center;
                if (toPoint.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                float alignment = Vector2.Dot(direction, toPoint.normalized);
                if (alignment <= bestAlignment)
                {
                    continue;
                }

                bestAlignment = alignment;
                nearestIndex = i;
            }

            return starPoints[nearestIndex];
        }

        private static bool TryRaySegmentIntersection(
            Vector2 rayOrigin,
            Vector2 rayDirection,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            out float distance,
            out Vector2 point)
        {
            distance = 0.0f;
            point = Vector2.zero;

            Vector2 segment = segmentEnd - segmentStart;
            float denominator = Cross(rayDirection, segment);
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                return false;
            }

            Vector2 toSegmentStart = segmentStart - rayOrigin;
            float rayDistance = Cross(toSegmentStart, segment) / denominator;
            float segmentT = Cross(toSegmentStart, rayDirection) / denominator;
            if (rayDistance < 0.0f || segmentT < 0.0f || segmentT > 1.0f)
            {
                return false;
            }

            distance = rayDistance;
            point = rayOrigin + (rayDirection * rayDistance);
            return true;
        }

        private Vector2 ViewportToMeshLocal(Vector2 viewportPoint, float meshPlaneDistance)
        {
            Vector3 worldPoint = targetCamera.ViewportToWorldPoint(
                new Vector3(viewportPoint.x, viewportPoint.y, meshPlaneDistance));
            Vector3 localPoint = targetMeshDeformer.transform.InverseTransformPoint(worldPoint);
            return new Vector2(localPoint.x, localPoint.y);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.x * b.y) - (a.y * b.x);
        }

        private static float Hash01(int value)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= 2747636419u;
                hash *= 2654435769u;
                hash ^= hash >> 16;
                hash *= 2654435769u;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFF) / 16777215.0f;
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

            AudioClip clip = AudioClip.Create("Mission1CompletionDing", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void PlayCompletionSparkle()
        {
            if (targetCamera == null)
            {
                return;
            }

            Bounds bounds = ResolveCompletionEffectBounds();
            GameObject sparkleObject = new GameObject("Mission1CompletionSparkle");
            sparkleObject.transform.position = bounds.center - (targetCamera.transform.forward * 0.08f);
            sparkleObject.transform.rotation = targetCamera.transform.rotation;

            ParticleSystem particles = sparkleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            float burstSeconds = Mathf.Max(0.05f, particleBurstSeconds);
            main.duration = burstSeconds;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(burstSeconds * 0.45f, burstSeconds);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.92f, 0.16f, 1.0f),
                new Color(1.0f, 0.64f, 0.0f, 1.0f));

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0.0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, (short)96) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.25f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.15f);

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 220;
            Material material = GetOrCreateSparkleMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            particles.Play();
            Destroy(sparkleObject, Mathf.Max(burstSeconds + 0.4f, completionEffectSeconds + 0.8f));
        }

        private Bounds ResolveCompletionEffectBounds()
        {
            if (targetMeshDeformer != null && targetMeshDeformer.HasMesh)
            {
                return targetMeshDeformer.GetWorldBounds();
            }

            GameObject resolvedShadowStar = ResolveShadowStarObject();
            if (resolvedShadowStar != null)
            {
                Renderer[] renderers = resolvedShadowStar.GetComponentsInChildren<Renderer>(true);
                bool hasBounds = false;
                Bounds combinedBounds = new Bounds(resolvedShadowStar.transform.position, Vector3.one);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        combinedBounds = renderers[i].bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(renderers[i].bounds);
                    }
                }

                return hasBounds
                    ? combinedBounds
                    : new Bounds(resolvedShadowStar.transform.position, Vector3.one);
            }

            return new Bounds(Vector3.zero, Vector3.one);
        }

        private Material GetOrCreateSparkleMaterial()
        {
            if (sparkleMaterial != null)
            {
                return sparkleMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return null;
            }

            sparkleMaterial = new Material(shader)
            {
                name = "Mission1CompletionSparkle_Runtime",
                hideFlags = HideFlags.DontSave
            };

            if (sparkleMaterial.HasProperty("_BaseColor"))
            {
                sparkleMaterial.SetColor("_BaseColor", new Color(1.0f, 0.84f, 0.0f, 1.0f));
            }

            if (sparkleMaterial.HasProperty("_Color"))
            {
                sparkleMaterial.SetColor("_Color", new Color(1.0f, 0.84f, 0.0f, 1.0f));
            }

            return sparkleMaterial;
        }

        private float EvaluateMatchScore()
        {
            if (!targetMeshDeformer.TryGetCurrentLocalTriangles(out Vector2[] localVertices, out int[] triangles) ||
                localVertices.Length == 0 ||
                triangles.Length < 3)
            {
                return 0.0f;
            }

            Vector2[] projectedVertices = ProjectVerticesToViewport(localVertices);
            TriangleBounds[] triangleBounds = BuildTriangleBounds(projectedVertices, triangles);

            float aspect = ResolveViewportAspect();
            int columns = Mathf.Max(1, Mathf.RoundToInt(sampleRows * aspect));
            int guideSamples = 0;
            int meshSamples = 0;
            int intersectionSamples = 0;

            for (int row = 0; row < sampleRows; row++)
            {
                float y = (row + 0.5f) / sampleRows;
                for (int column = 0; column < columns; column++)
                {
                    Vector2 samplePoint = new Vector2((column + 0.5f) / columns, y);
                    bool insideGuide = IsPointInsidePolygon(samplePoint, guidePolygon);
                    bool insideMesh = IsPointInsideProjectedMesh(samplePoint, projectedVertices, triangles, triangleBounds);

                    if (insideGuide)
                    {
                        guideSamples++;
                    }

                    if (insideMesh)
                    {
                        meshSamples++;
                    }

                    if (insideGuide && insideMesh)
                    {
                        intersectionSamples++;
                    }
                }
            }

            if (guideSamples == 0 || meshSamples == 0 || intersectionSamples == 0)
            {
                return 0.0f;
            }

            float guideCoverage = intersectionSamples / (float)guideSamples;
            float meshPrecision = intersectionSamples / (float)meshSamples;
            float denominator = guideCoverage + meshPrecision;
            if (denominator <= 0.000001f)
            {
                return 0.0f;
            }

            return (2.0f * guideCoverage * meshPrecision) / denominator;
        }

        private Vector2[] ProjectVerticesToViewport(Vector2[] localVertices)
        {
            Vector2[] projectedVertices = new Vector2[localVertices.Length];
            for (int i = 0; i < localVertices.Length; i++)
            {
                Vector3 localPoint = new Vector3(localVertices[i].x, localVertices[i].y, 0.0f);
                Vector3 worldPoint = targetMeshDeformer.transform.TransformPoint(localPoint);
                Vector3 viewportPoint = targetCamera.WorldToViewportPoint(worldPoint);
                projectedVertices[i] = new Vector2(viewportPoint.x, viewportPoint.y);
            }

            return projectedVertices;
        }

        private TriangleBounds[] BuildTriangleBounds(Vector2[] projectedVertices, int[] triangles)
        {
            int triangleCount = triangles.Length / 3;
            TriangleBounds[] bounds = new TriangleBounds[triangleCount];
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int baseIndex = triangleIndex * 3;
                Vector2 a = projectedVertices[triangles[baseIndex]];
                Vector2 b = projectedVertices[triangles[baseIndex + 1]];
                Vector2 c = projectedVertices[triangles[baseIndex + 2]];

                bounds[triangleIndex] = new TriangleBounds
                {
                    MinX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)),
                    MaxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                    MinY = Mathf.Min(a.y, Mathf.Min(b.y, c.y)),
                    MaxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y))
                };
            }

            return bounds;
        }

        private bool IsPointInsideProjectedMesh(
            Vector2 point,
            Vector2[] projectedVertices,
            int[] triangles,
            TriangleBounds[] triangleBounds)
        {
            int triangleCount = triangles.Length / 3;
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                TriangleBounds bounds = triangleBounds[triangleIndex];
                if (point.x < bounds.MinX || point.x > bounds.MaxX || point.y < bounds.MinY || point.y > bounds.MaxY)
                {
                    continue;
                }

                int baseIndex = triangleIndex * 3;
                if (IsPointInsideTriangle(
                        point,
                        projectedVertices[triangles[baseIndex]],
                        projectedVertices[triangles[baseIndex + 1]],
                        projectedVertices[triangles[baseIndex + 2]]))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildGuidePolygonIfNeeded(bool force)
        {
            float aspect = ResolveViewportAspect();
            if (TryBuildIntroStarGuideViewportPoints(out Vector2[] starGuidePolygon))
            {
                guidePolygon = starGuidePolygon;
                guidePolygonAspect = aspect;
                return;
            }

            if (!force && guidePolygon.Length == StarPointCount && Mathf.Abs(guidePolygonAspect - aspect) <= 0.001f)
            {
                return;
            }

            guidePolygon = BuildStarPolygonViewport(aspect);
            guidePolygonAspect = aspect;
        }

        private Vector2[] BuildStarPolygonViewport(float aspect)
        {
            Vector2[] points = new Vector2[StarPointCount];
            Vector2 center = new Vector2(0.5f, 0.5f);
            float outerRadiusX = outerRadiusViewportY / Mathf.Max(0.01f, aspect);
            float innerRadiusX = innerRadiusViewportY / Mathf.Max(0.01f, aspect);

            for (int i = 0; i < points.Length; i++)
            {
                bool outerPoint = i % 2 == 0;
                float radiusX = outerPoint ? outerRadiusX : innerRadiusX;
                float radiusY = outerPoint ? outerRadiusViewportY : innerRadiusViewportY;
                float angle = (Mathf.PI * 0.5f) + (i * Mathf.PI / 5.0f);
                points[i] = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
            }

            return points;
        }

        private float ResolveViewportAspect()
        {
            if (targetCamera != null && targetCamera.pixelHeight > 0)
            {
                return Mathf.Max(0.01f, targetCamera.pixelWidth / (float)targetCamera.pixelHeight);
            }

            return 16.0f / 9.0f;
        }

        private void CreateGuideLine()
        {
            guideLineObject = new GameObject("Mission1StarGuideLine");
            guideLineObject.transform.SetParent(transform, false);

            guideLineMaterial = CreateUnlitMaterial(guideColor);
            UpdateGuideLine();
        }

        private void UpdateGuideLine()
        {
            if (guideLineObject == null || targetCamera == null || guidePolygon.Length != StarPointCount)
            {
                return;
            }

            float guideDistance = ResolveGuideDistance();
            float worldHeight = targetCamera.orthographic
                ? targetCamera.orthographicSize * 2.0f
                : Mathf.Max(1.0f, guideDistance);
            float pixelHeight = Mathf.Max(1.0f, targetCamera.pixelHeight);
            float worldUnitsPerPixel = worldHeight / pixelHeight;
            float lineWidth = Mathf.Max(0.001f, guideLineWidthPixels * worldUnitsPerPixel);
            float dashLength = Mathf.Max(lineWidth, guideDashLengthPixels * worldUnitsPerPixel);
            float gapLength = Mathf.Max(lineWidth, guideGapLengthPixels * worldUnitsPerPixel);

            Vector3[] worldPoints = TryBuildIntroStarGuideWorldPoints(out Vector3[] starWorldPoints)
                ? starWorldPoints
                : BuildViewportGuideWorldPoints(guideDistance);

            int usedDashCount = 0;
            for (int i = 0; i < worldPoints.Length; i++)
            {
                Vector3 start = worldPoints[i];
                Vector3 end = worldPoints[(i + 1) % worldPoints.Length];
                usedDashCount = AddGuideDashesForSegment(start, end, dashLength, gapLength, lineWidth, usedDashCount);
            }

            for (int i = usedDashCount; i < guideDashRenderers.Count; i++)
            {
                guideDashRenderers[i].gameObject.SetActive(false);
            }
        }

        private Vector3[] BuildViewportGuideWorldPoints(float guideDistance)
        {
            Vector3[] worldPoints = new Vector3[guidePolygon.Length];
            for (int i = 0; i < guidePolygon.Length; i++)
            {
                Vector2 viewportPoint = guidePolygon[i];
                worldPoints[i] = targetCamera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, guideDistance));
            }

            return worldPoints;
        }

        private bool TryBuildIntroStarGuideViewportPoints(out Vector2[] viewportPoints)
        {
            viewportPoints = null;
            if (targetCamera == null || !TryBuildIntroStarGuideWorldPoints(out Vector3[] worldPoints))
            {
                return false;
            }

            viewportPoints = new Vector2[worldPoints.Length];
            for (int i = 0; i < worldPoints.Length; i++)
            {
                Vector3 viewportPoint = targetCamera.WorldToViewportPoint(worldPoints[i]);
                viewportPoints[i] = new Vector2(viewportPoint.x, viewportPoint.y);
            }

            return true;
        }

        private bool TryBuildIntroStarGuideWorldPoints(out Vector3[] worldPoints)
        {
            worldPoints = null;
            Transform starTransform = ResolveIntroStarTransform();
            if (starTransform == null || !starTransform.gameObject.activeInHierarchy)
            {
                return false;
            }

            MeshFilter starMeshFilter = starTransform.GetComponent<MeshFilter>();
            Mesh starMesh = starMeshFilter != null ? starMeshFilter.sharedMesh : null;
            if (starMesh == null || starMesh.vertexCount <= StarPointCount)
            {
                return false;
            }

            Vector3[] vertices = starMesh.vertices;
            worldPoints = new Vector3[StarPointCount];
            for (int i = 0; i < StarPointCount; i++)
            {
                worldPoints[i] = starTransform.TransformPoint(vertices[i + 1]);
            }

            return true;
        }

        private int AddGuideDashesForSegment(
            Vector3 start,
            Vector3 end,
            float dashLength,
            float gapLength,
            float lineWidth,
            int nextDashIndex)
        {
            Vector3 segment = end - start;
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.0001f)
            {
                return nextDashIndex;
            }

            Vector3 direction = segment / segmentLength;
            float cursor = 0.0f;
            while (cursor < segmentLength)
            {
                float dashEnd = Mathf.Min(cursor + dashLength, segmentLength);
                if (dashEnd > cursor)
                {
                    LineRenderer dashRenderer = GetOrCreateGuideDashRenderer(nextDashIndex++);
                    dashRenderer.gameObject.SetActive(true);
                    dashRenderer.widthMultiplier = lineWidth;
                    dashRenderer.sortingOrder = guideSortingOrder;
                    dashRenderer.SetPosition(0, start + (direction * cursor));
                    dashRenderer.SetPosition(1, start + (direction * dashEnd));
                }

                cursor += dashLength + gapLength;
            }

            return nextDashIndex;
        }

        private LineRenderer GetOrCreateGuideDashRenderer(int index)
        {
            while (guideDashRenderers.Count <= index)
            {
                GameObject dashObject = new GameObject($"Mission1StarGuideDash_{guideDashRenderers.Count:00}");
                dashObject.transform.SetParent(guideLineObject.transform, false);

                LineRenderer dashRenderer = dashObject.AddComponent<LineRenderer>();
                dashRenderer.loop = false;
                dashRenderer.useWorldSpace = true;
                dashRenderer.positionCount = 2;
                dashRenderer.numCapVertices = 2;
                dashRenderer.numCornerVertices = 0;
                dashRenderer.alignment = LineAlignment.View;
                dashRenderer.textureMode = LineTextureMode.Stretch;
                dashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dashRenderer.receiveShadows = false;
                dashRenderer.sortingOrder = guideSortingOrder;
                dashRenderer.sharedMaterial = guideLineMaterial;
                guideDashRenderers.Add(dashRenderer);
            }

            return guideDashRenderers[index];
        }

        private float ResolveGuideDistance()
        {
            Vector3 guideCenter = Vector3.zero;
            if (targetMeshDeformer != null && targetMeshDeformer.HasMesh)
            {
                guideCenter = targetMeshDeformer.GetWorldBounds().center;
            }

            float meshDistance = Mathf.Abs(Vector3.Dot(
                guideCenter - targetCamera.transform.position,
                targetCamera.transform.forward));
            return Mathf.Max(targetCamera.nearClipPlane, meshDistance - guideDepthOffsetInFrontOfMesh);
        }

        private void UpdateMatchProgressBar(float matchScore)
        {
            float normalizedScore = Mathf.Clamp01(matchScore);
            float displayScore = Mathf.Clamp01(normalizedScore / Mathf.Max(0.0001f, matchThreshold));

            if (matchProgressFillRect == null && matchThresholdMarker == null)
            {
                return;
            }

            if (matchProgressFillRect != null)
            {
                RectTransform parentRect = matchProgressFillRect.parent as RectTransform;
                float parentWidth = ResolveProgressBarWidth(parentRect);

                matchProgressFillRect.anchorMin = new Vector2(0.0f, 0.0f);
                matchProgressFillRect.anchorMax = new Vector2(0.0f, 1.0f);
                matchProgressFillRect.pivot = new Vector2(0.0f, 0.5f);
                matchProgressFillRect.anchoredPosition = Vector2.zero;
                matchProgressFillRect.sizeDelta = new Vector2(parentWidth * displayScore, 0.0f);
            }

            if (matchThresholdMarker != null)
            {
                RectTransform parentRect = matchThresholdMarker.parent as RectTransform;
                float parentWidth = ResolveProgressBarWidth(parentRect);
                matchThresholdMarker.anchoredPosition = new Vector2(parentWidth, 0.0f);
            }
        }

        private static float ResolveProgressBarWidth(RectTransform parentRect)
        {
            if (parentRect != null && parentRect.rect.width > 0.0f)
            {
                return parentRect.rect.width;
            }

            return 720.0f;
        }

        private static bool IsPointInsidePolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool intersects = (a.y > point.y) != (b.y > point.y) &&
                    point.x < ((b.x - a.x) * (point.y - a.y) / ((b.y - a.y) + 0.000001f)) + a.x;
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool IsPointInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float denominator = ((b.y - c.y) * (a.x - c.x)) + ((c.x - b.x) * (a.y - c.y));
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                return false;
            }

            float alpha = (((b.y - c.y) * (point.x - c.x)) + ((c.x - b.x) * (point.y - c.y))) / denominator;
            float beta = (((c.y - a.y) * (point.x - c.x)) + ((a.x - c.x) * (point.y - c.y))) / denominator;
            float gamma = 1.0f - alpha - beta;
            return alpha >= 0.0f && beta >= 0.0f && gamma >= 0.0f;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material material = new Material(shader)
            {
                name = "Mission1StarGuideLine_Runtime",
                hideFlags = HideFlags.DontSave
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            ConfigureTransparentOverlayMaterial(material);
            return material;
        }

        private static void ConfigureTransparentOverlayMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1.0f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0.0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0.0f);
            }

            if (material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3100;
        }

        private void OnDestroy()
        {
            DestroyRecreatedIntroStarObject();
            DestroyRuntimeObject(guideLineMaterial);
            DestroyRuntimeObject(sparkleMaterial);
            DestroyRuntimeObject(completionDingClip);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
        {
            if (runtimeObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeObject);
            }
            else
            {
                DestroyImmediate(runtimeObject);
            }
        }

        private struct TriangleBounds
        {
            public float MinX;
            public float MaxX;
            public float MinY;
            public float MaxY;
        }

        private struct MorphVertexTarget
        {
            public Vector3 TargetLocal;
            public float Delay;
            public float Phase;
            public Vector2 WobbleDirection;
            public float WobbleAmplitude;
        }
    }

    internal sealed class SceneLoadDeferredDestroyer : MonoBehaviour
    {
        private int framesToKeepAfterSceneLoad = 1;
        private readonly List<UnityEngine.Object> runtimeObjectsToDestroy = new List<UnityEngine.Object>();

        public void RegisterRuntimeObject(UnityEngine.Object runtimeObject)
        {
            if (runtimeObject == null || runtimeObjectsToDestroy.Contains(runtimeObject))
            {
                return;
            }

            runtimeObjectsToDestroy.Add(runtimeObject);
        }

        public void DestroyAfterNextSceneLoad(int framesToKeep)
        {
            framesToKeepAfterSceneLoad = Mathf.Max(1, framesToKeep);
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            DestroyRegisteredRuntimeObjects();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            StartCoroutine(DestroyAfterFramesRoutine());
        }

        private IEnumerator DestroyAfterFramesRoutine()
        {
            for (int i = 0; i < framesToKeepAfterSceneLoad; i++)
            {
                yield return new WaitForEndOfFrame();
            }

            Destroy(gameObject);
        }

        private void DestroyRegisteredRuntimeObjects()
        {
            for (int i = 0; i < runtimeObjectsToDestroy.Count; i++)
            {
                UnityEngine.Object runtimeObject = runtimeObjectsToDestroy[i];
                if (runtimeObject == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(runtimeObject);
                }
                else
                {
                    DestroyImmediate(runtimeObject);
                }
            }

            runtimeObjectsToDestroy.Clear();
        }
    }
}
