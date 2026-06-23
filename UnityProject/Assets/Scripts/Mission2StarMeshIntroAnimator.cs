using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public sealed class Mission2StarMeshIntroAnimator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const int MinimumSunSlideCueRepeatCount = 3;

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

        [Header("Interaction Hint")]
        [SerializeField, Min(0.0f)] private float interactionHintDelaySeconds = 10.0f;
        [SerializeField] private string interactionHintMessage = "\uBE5B\uC774 \uAC00\uAE4C\uC6CC\uC9C8\uC218\uB85D ...";
        [SerializeField] private Color interactionHintTextColor = Color.black;
        [SerializeField, Min(12)] private int interactionHintFontSize = 36;
        [SerializeField] private GameObject interactionHintObject;
        [SerializeField] private Text interactionHintTextComponent;

        [Header("Interaction Start Cue")]
        [SerializeField] private bool playSunSlideCueOnInteractionStart = true;
        [SerializeField, Min(1)] private int sunSlideCueRepeatCount = 3;
        [SerializeField, Min(0.0f)] private float sunSlideCueDistanceX = 3.3f;
        [SerializeField, Min(0.01f)] private float sunSlideCueSlideSeconds = 0.84f;
        [SerializeField, Min(0.0f)] private float sunSlideCueReturnSeconds = 0.36f;
        [SerializeField, Min(0.0f)] private float sunSlideCuePauseSeconds = 0.28f;
        [SerializeField, Range(0.0f, 1.0f)] private float sunSlideCueGhostAlpha = 0.28f;
        [SerializeField, Min(0)] private int sunSlideCueGhostCount = 5;
        [SerializeField, Min(0.01f)] private float sunSlideCueGhostLifetimeSeconds = 0.55f;
        [SerializeField] private bool animateShadowStarScaleWithSunSlideCue = true;
        [SerializeField, Min(1.0f)] private float sunSlideCueShadowStarScaleMultiplier = 1.25f;
        [SerializeField] private bool handDetectionCancelsSunSlideCue = true;

        private Coroutine animationRoutine;
        private Coroutine interactionStartCueRoutine;
        private Coroutine interactionHintRoutine;
        private Mission2Phase currentPhase;
        private MaterialPropertyBlock darkPropertyBlock;
        private MaterialPropertyBlock backgroundPropertyBlock;
        private bool sunSlideCueInterruptedByHand;
        private readonly List<GameObject> sunSlideGhostObjects = new List<GameObject>();

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
            SetInteractionHintVisible(false);
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
            StopInteractionStartCue();
            StopInteractionHint();
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
            SetInteractionHintVisible(false);
        }

        public void EnterInteraction()
        {
            currentPhase = Mission2Phase.Interaction;
            SetDarkAlpha(0.0f);
            SetIntroBackgroundAlpha(0.0f);
            SetInteractionInstructionVisible(true);
            StartMediaPipeTracking();

            StopInteractionStartCue();
            StopInteractionHint();
            SetInteractionHintVisible(false);
            if (playSunSlideCueOnInteractionStart && sunSlideCueRepeatCount > 0)
            {
                if (sunHandSystem != null)
                {
                    sunHandSystem.enabled = false;
                }

                interactionStartCueRoutine = StartCoroutine(PlayInteractionStartCueThenBeginRoutine());
                return;
            }

            BeginSunInteraction();
        }

        private IEnumerator PlayInteractionStartCueThenBeginRoutine()
        {
            yield return PlaySunSlideCueRoutine();
            interactionStartCueRoutine = null;

            if (currentPhase == Mission2Phase.Interaction)
            {
                BeginSunInteraction();
            }
        }

        private void BeginSunInteraction()
        {
            if (sunHandSystem != null)
            {
                sunHandSystem.BeginInteraction();
            }

            StartInteractionHintTimer();
        }

        public void HideInteractionInstruction()
        {
            StopInteractionHint();
            SetInteractionInstructionVisible(false);
            SetInteractionHintVisible(false);
        }

        public void EnterOutro()
        {
            StopInteractionStartCue();
            StopInteractionHint();
            currentPhase = Mission2Phase.Outro;
            SetInteractionInstructionVisible(false);
            SetInteractionHintVisible(false);
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

        public void DebugAdvance()
        {
            if (currentPhase == Mission2Phase.Intro)
            {
                if (animationRoutine != null)
                {
                    StopCoroutine(animationRoutine);
                    animationRoutine = null;
                }

                EnterInteraction();
                return;
            }

            Mission2SunHandSystem handSystem = sunHandSystem != null
                ? sunHandSystem
                : FindObjectOfType<Mission2SunHandSystem>();
            handSystem?.DebugCompleteMission();
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

        private IEnumerator PlaySunSlideCueRoutine()
        {
            Transform sunTransform = ResolveIntroSunTransform();
            if (sunTransform == null)
            {
                yield break;
            }

            Vector3 restPosition = sunTransform.position;
            Transform cueShadowTransform = animateShadowStarScaleWithSunSlideCue ? ResolveShadowStarTransform() : null;
            Vector3 cueShadowRestScale = cueShadowTransform != null ? cueShadowTransform.localScale : Vector3.one;
            sunSlideCueInterruptedByHand = false;
            int repeatCount = Mathf.Max(MinimumSunSlideCueRepeatCount, sunSlideCueRepeatCount);
            float slideDistance = Mathf.Max(0.0f, sunSlideCueDistanceX);
            float slideDuration = Mathf.Max(0.01f, sunSlideCueSlideSeconds);
            float returnDuration = Mathf.Max(0.0f, sunSlideCueReturnSeconds);
            float pauseDuration = Mathf.Max(0.0f, sunSlideCuePauseSeconds);

            for (int repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
            {
                if (currentPhase != Mission2Phase.Interaction)
                {
                    RestoreSunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale);
                    yield break;
                }

                sunTransform.position = restPosition;
                Vector3 targetPosition = restPosition + (Vector3.left * slideDistance);
                if (TryInterruptSunSlideCueForHand(cueShadowTransform, cueShadowRestScale))
                {
                    yield break;
                }

                yield return SlideSunWithGhostsRoutine(
                    sunTransform,
                    restPosition,
                    targetPosition,
                    slideDuration,
                    cueShadowTransform,
                    cueShadowRestScale);
                if (sunSlideCueInterruptedByHand)
                {
                    yield break;
                }

                if (pauseDuration > 0.0f)
                {
                    yield return WaitSunSlideCuePauseRoutine(pauseDuration, cueShadowTransform, cueShadowRestScale);
                    if (sunSlideCueInterruptedByHand)
                    {
                        yield break;
                    }
                }

                yield return MoveSunRoutine(
                    sunTransform,
                    targetPosition,
                    restPosition,
                    returnDuration,
                    cueShadowTransform,
                    cueShadowRestScale,
                    1.0f,
                    0.0f);
                if (sunSlideCueInterruptedByHand)
                {
                    yield break;
                }

                if (pauseDuration > 0.0f && repeatIndex < repeatCount - 1)
                {
                    yield return WaitSunSlideCuePauseRoutine(pauseDuration, cueShadowTransform, cueShadowRestScale);
                    if (sunSlideCueInterruptedByHand)
                    {
                        yield break;
                    }
                }
            }

            sunTransform.position = restPosition;
            RestoreSunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale);
            if (pauseDuration > 0.0f)
            {
                yield return WaitSunSlideCuePauseRoutine(pauseDuration, cueShadowTransform, cueShadowRestScale);
            }
        }

        private IEnumerator SlideSunWithGhostsRoutine(
            Transform sunTransform,
            Vector3 startPosition,
            Vector3 targetPosition,
            float durationSeconds,
            Transform cueShadowTransform,
            Vector3 cueShadowRestScale)
        {
            int ghostCount = Mathf.Max(0, sunSlideCueGhostCount);
            float nextGhostTime = ghostCount > 0 ? durationSeconds / (ghostCount + 1) : float.PositiveInfinity;
            int spawnedGhostCount = 0;
            float elapsed = 0.0f;

            SpawnSunSlideGhost(sunTransform);
            while (elapsed < durationSeconds)
            {
                if (TryInterruptSunSlideCueForHand(cueShadowTransform, cueShadowRestScale))
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / durationSeconds);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                sunTransform.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                ApplySunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale, eased);

                if (spawnedGhostCount < ghostCount && elapsed >= nextGhostTime)
                {
                    SpawnSunSlideGhost(sunTransform);
                    spawnedGhostCount++;
                    nextGhostTime = durationSeconds * (spawnedGhostCount + 1) / (ghostCount + 1);
                }

                yield return null;
            }

            sunTransform.position = targetPosition;
            ApplySunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale, 1.0f);
        }

        private IEnumerator MoveSunRoutine(
            Transform sunTransform,
            Vector3 startPosition,
            Vector3 targetPosition,
            float durationSeconds,
            Transform cueShadowTransform,
            Vector3 cueShadowRestScale,
            float shadowStartAmount,
            float shadowTargetAmount)
        {
            if (sunTransform == null)
            {
                yield break;
            }

            if (durationSeconds <= 0.0f)
            {
                sunTransform.position = targetPosition;
                ApplySunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale, shadowTargetAmount);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < durationSeconds)
            {
                if (TryInterruptSunSlideCueForHand(cueShadowTransform, cueShadowRestScale))
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / durationSeconds);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                sunTransform.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                float shadowAmount = Mathf.LerpUnclamped(shadowStartAmount, shadowTargetAmount, eased);
                ApplySunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale, shadowAmount);
                yield return null;
            }

            sunTransform.position = targetPosition;
            ApplySunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale, shadowTargetAmount);
        }

        private IEnumerator WaitSunSlideCuePauseRoutine(
            float durationSeconds,
            Transform cueShadowTransform,
            Vector3 cueShadowRestScale)
        {
            float elapsed = 0.0f;
            while (elapsed < durationSeconds)
            {
                if (TryInterruptSunSlideCueForHand(cueShadowTransform, cueShadowRestScale))
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private bool TryInterruptSunSlideCueForHand(Transform cueShadowTransform, Vector3 cueShadowRestScale)
        {
            if (!handDetectionCancelsSunSlideCue || !IsInteractionHandVisible())
            {
                return false;
            }

            sunSlideCueInterruptedByHand = true;
            ClearSunSlideGhosts();
            RestoreSunSlideCueShadowScale(cueShadowTransform, cueShadowRestScale);
            return true;
        }

        private bool IsInteractionHandVisible()
        {
            return mediaPipeReceiver != null &&
                   mediaPipeReceiver.enabled &&
                   mediaPipeReceiver.HasRecentData;
        }

        private void ApplySunSlideCueShadowScale(
            Transform cueShadowTransform,
            Vector3 cueShadowRestScale,
            float slideAmount)
        {
            if (cueShadowTransform == null || !animateShadowStarScaleWithSunSlideCue)
            {
                return;
            }

            float easedAmount = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(slideAmount));
            float scaleMultiplier = Mathf.LerpUnclamped(
                1.0f,
                Mathf.Max(1.0f, sunSlideCueShadowStarScaleMultiplier),
                easedAmount);
            ApplyScaleWithBottomAnchor(cueShadowTransform, cueShadowRestScale * scaleMultiplier);
        }

        private void RestoreSunSlideCueShadowScale(Transform cueShadowTransform, Vector3 cueShadowRestScale)
        {
            if (cueShadowTransform == null)
            {
                return;
            }

            ApplyScaleWithBottomAnchor(cueShadowTransform, cueShadowRestScale);
        }

        private static void ApplyScaleWithBottomAnchor(Transform rootTransform, Vector3 targetScale)
        {
            if (rootTransform == null)
            {
                return;
            }

            Vector3 bottomAnchorBefore = GetShadowBottomAnchor(rootTransform);
            rootTransform.localScale = targetScale;
            Vector3 bottomAnchorAfter = GetShadowBottomAnchor(rootTransform);
            Vector3 correction = bottomAnchorBefore - bottomAnchorAfter;
            correction.z = 0.0f;
            rootTransform.position += correction;
        }

        private static Vector3 GetShadowBottomAnchor(Transform rootTransform)
        {
            Renderer renderer = rootTransform != null ? rootTransform.GetComponentInChildren<Renderer>() : null;
            if (renderer != null && renderer.bounds.size.sqrMagnitude > 0.0001f)
            {
                Bounds bounds = renderer.bounds;
                return new Vector3(bounds.center.x, bounds.min.y, rootTransform.position.z);
            }

            return rootTransform != null ? rootTransform.position : Vector3.zero;
        }

        private void SpawnSunSlideGhost(Transform sourceTransform)
        {
            if (sourceTransform == null || sunSlideCueGhostAlpha <= 0.0f)
            {
                return;
            }

            MeshFilter sourceMeshFilter = sourceTransform.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = sourceTransform.GetComponent<MeshRenderer>();
            if (sourceMeshFilter == null || sourceRenderer == null || sourceMeshFilter.sharedMesh == null)
            {
                return;
            }

            Material ghostMaterial = CreateSunSlideGhostMaterial(sourceRenderer, sunSlideCueGhostAlpha);
            if (ghostMaterial == null)
            {
                return;
            }

            GameObject ghostObject = new GameObject("SunSlideGhost");
            ghostObject.hideFlags = HideFlags.DontSave;
            ghostObject.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            ghostObject.transform.localScale = sourceTransform.lossyScale;

            MeshFilter ghostMeshFilter = ghostObject.AddComponent<MeshFilter>();
            ghostMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

            MeshRenderer ghostRenderer = ghostObject.AddComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = ghostMaterial;
            ghostRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            ghostRenderer.sortingOrder = sourceRenderer.sortingOrder - 1;
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghostRenderer.receiveShadows = false;

            sunSlideGhostObjects.Add(ghostObject);
            StartCoroutine(FadeAndDestroySunSlideGhostRoutine(
                ghostObject,
                ghostRenderer,
                ghostMaterial,
                sunSlideCueGhostAlpha,
                sunSlideCueGhostLifetimeSeconds));
        }

        private IEnumerator FadeAndDestroySunSlideGhostRoutine(
            GameObject ghostObject,
            Renderer ghostRenderer,
            Material ghostMaterial,
            float startAlpha,
            float lifetimeSeconds)
        {
            float duration = Mathf.Max(0.01f, lifetimeSeconds);
            float elapsed = 0.0f;
            while (elapsed < duration && ghostObject != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(startAlpha, 0.0f, t);
                SetMaterialAlpha(ghostMaterial, alpha);
                yield return null;
            }

            sunSlideGhostObjects.Remove(ghostObject);
            DestroyRuntimeObject(ghostMaterial);
            if (ghostObject != null)
            {
                Destroy(ghostObject);
            }
        }

        private static Material CreateSunSlideGhostMaterial(Renderer sourceRenderer, float alpha)
        {
            Shader shader = null;
            Material sourceMaterial = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
            if (sourceMaterial != null)
            {
                shader = sourceMaterial.shader;
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = "Mission2SunSlideGhost_Runtime",
                hideFlags = HideFlags.DontSave
            };
            Color color = ResolveRendererColor(sourceMaterial);
            color.a = Mathf.Clamp01(alpha);
            SetMaterialColor(material, color);
            ConfigureTransparentMaterial(material);
            return material;
        }

        private static Color ResolveRendererColor(Material material)
        {
            if (material != null)
            {
                if (material.HasProperty(BaseColorId))
                {
                    return material.GetColor(BaseColorId);
                }

                if (material.HasProperty(ColorId))
                {
                    return material.GetColor(ColorId);
                }
            }

            return new Color(0.105f, 0.07f, 0.045f, 1.0f);
        }

        private static void ConfigureTransparentMaterial(Material material)
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

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0.0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void SetMaterialAlpha(Material material, float alpha)
        {
            if (material == null)
            {
                return;
            }

            Color color = ResolveRendererColor(material);
            color.a = Mathf.Clamp01(alpha);
            SetMaterialColor(material, color);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
        }

        private void StopInteractionStartCue()
        {
            if (interactionStartCueRoutine != null)
            {
                StopCoroutine(interactionStartCueRoutine);
                interactionStartCueRoutine = null;
            }

            ClearSunSlideGhosts();
        }

        private void ClearSunSlideGhosts()
        {
            for (int i = sunSlideGhostObjects.Count - 1; i >= 0; i--)
            {
                GameObject ghostObject = sunSlideGhostObjects[i];
                if (ghostObject == null)
                {
                    continue;
                }

                Renderer renderer = ghostObject.GetComponent<Renderer>();
                Material material = renderer != null ? renderer.sharedMaterial : null;
                DestroyRuntimeObject(material);
                DestroyRuntimeObject(ghostObject);
            }

            sunSlideGhostObjects.Clear();
        }

        private static void DestroyRuntimeObject(Object targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(targetObject);
            }
            else
            {
                Object.DestroyImmediate(targetObject);
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

        private void StartInteractionHintTimer()
        {
            StopInteractionHint();
            SetInteractionHintVisible(false);

            if (interactionHintDelaySeconds <= 0.0f)
            {
                SetInteractionHintVisible(currentPhase == Mission2Phase.Interaction);
                return;
            }

            interactionHintRoutine = StartCoroutine(ShowInteractionHintAfterDelayRoutine());
        }

        private IEnumerator ShowInteractionHintAfterDelayRoutine()
        {
            float elapsed = 0.0f;
            float delay = Mathf.Max(0.0f, interactionHintDelaySeconds);
            while (elapsed < delay)
            {
                if (currentPhase != Mission2Phase.Interaction)
                {
                    interactionHintRoutine = null;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            interactionHintRoutine = null;
            SetInteractionHintVisible(currentPhase == Mission2Phase.Interaction);
        }

        private void StopInteractionHint()
        {
            if (interactionHintRoutine == null)
            {
                return;
            }

            StopCoroutine(interactionHintRoutine);
            interactionHintRoutine = null;
        }

        private void SetInteractionHintVisible(bool isVisible)
        {
            ResolveInteractionHintReferences();

            if (isVisible)
            {
                ApplyInteractionHintSettings();
            }

            if (interactionHintObject != null)
            {
                interactionHintObject.SetActive(isVisible);
            }
            else if (interactionHintTextComponent != null)
            {
                interactionHintTextComponent.gameObject.SetActive(isVisible);
            }
        }

        private void ResolveInteractionHintReferences()
        {
            if (interactionHintTextComponent == null && interactionHintObject != null)
            {
                interactionHintTextComponent = interactionHintObject.GetComponentInChildren<Text>(true);
            }

            if (interactionHintObject == null && interactionHintTextComponent != null)
            {
                interactionHintObject = interactionHintTextComponent.gameObject;
            }
        }

        private void ApplyInteractionHintSettings()
        {
            if (interactionHintTextComponent == null)
            {
                return;
            }

            interactionHintTextComponent.text = interactionHintMessage;
            interactionHintTextComponent.color = interactionHintTextColor;
            interactionHintTextComponent.fontSize = Mathf.Max(12, interactionHintFontSize);
            interactionHintTextComponent.alignment = TextAnchor.MiddleCenter;
            interactionHintTextComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            interactionHintTextComponent.verticalOverflow = VerticalWrapMode.Truncate;
            interactionHintTextComponent.raycastTarget = false;
            interactionHintTextComponent.supportRichText = false;
            interactionHintTextComponent.font = ResolveInteractionInstructionFont();
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
            Quaternion startRotation = targetTransform.localRotation;
            float duration = Mathf.Max(0.0f, durationSeconds);
            if (duration <= 0.0f)
            {
                StarWalkMotion.FinishWorld(targetTransform, targetPosition, startRotation);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
                Vector3 framePosition = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                StarWalkMotion.ApplyWorld(
                    targetTransform,
                    framePosition,
                    startPosition,
                    targetPosition,
                    eased,
                    startRotation);
                yield return null;
            }

            StarWalkMotion.FinishWorld(targetTransform, targetPosition, startRotation);
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
            Quaternion startRotation = resolvedShadowStar.localRotation;
            Vector3 targetPosition = startPosition;
            targetPosition.x = targetX;

            float duration = Mathf.Max(0.0f, durationSeconds);
            if (duration <= 0.0f)
            {
                StarWalkMotion.FinishWorld(resolvedShadowStar, targetPosition, startRotation);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = easeIn ? t * t : Mathf.SmoothStep(0.0f, 1.0f, t);
                Vector3 framePosition = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
                StarWalkMotion.ApplyWorld(
                    resolvedShadowStar,
                    framePosition,
                    startPosition,
                    targetPosition,
                    eased,
                    startRotation);
                yield return null;
            }

            StarWalkMotion.FinishWorld(resolvedShadowStar, targetPosition, startRotation);
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
