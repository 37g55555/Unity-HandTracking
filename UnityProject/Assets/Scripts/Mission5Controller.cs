using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public sealed class Mission5Controller : MonoBehaviour
    {
        public enum Mission5Phase
        {
            Intro,
            Interaction,
            Outro
        }

        [Header("Phase")]
        [SerializeField] private Mission5Phase initialPhase = Mission5Phase.Intro;
        [SerializeField] private bool autoEnterInteractionAfterIntro = true;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool setMission5StateOnStart = true;

        [Header("Intro")]
        [SerializeField] private Image fadeOverlayImage;
        [SerializeField, Min(0.0f)] private float introFadeSeconds = 2.0f;
        [SerializeField] private Transform shadowStarTransform;
        [SerializeField] private float introShadowStarTargetY = -4.4f;
        [SerializeField, Min(0.0f)] private float introShadowStarMoveSeconds = 2.0f;
        [SerializeField] private NarrationSubtitleSequencePlayer introNarrationPlayer;
        [SerializeField, Min(0.0f)] private float introNarrationStartDelaySeconds = 1.0f;
        [SerializeField, Min(0)] private int introNarrationStartStepIndex;
        [SerializeField, Min(0)] private int introNarrationStepCount = 3;
        [SerializeField] private Transform wallRoot;
        [SerializeField, Min(0.0f)] private float wallFadeOutSeconds = 2.0f;
        [SerializeField] private Vector3 interactionShadowStarPosition = new Vector3(-4.5f, -3.35f, 0.02f);
        [SerializeField, Min(0.0f)] private float interactionShadowStarJumpSeconds = 0.6f;
        [SerializeField, Min(0.0f)] private float interactionShadowStarJumpHeight = 0.6f;

        [Header("Outro")]
        [SerializeField, Min(0.0f)] private float outroDuration = 2.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float outroWallTargetAlpha = 210.0f / 255.0f;
        [SerializeField, Min(0.0f)] private float outroJumpDelaySeconds = 1.0f;
        [SerializeField] private Vector3 outroShadowStarPosition = new Vector3(-4.0f, 1.3f, 0.02f);
        [SerializeField, Min(0.0f)] private float outroShadowStarJumpSeconds = 1.0f;
        [SerializeField, Min(0.0f)] private float outroShadowStarScale = 0.5f;
        [SerializeField, Min(0.0f)] private float outroShadowStarJumpHeight = 1.6f;

        [Header("Interaction")]
        [SerializeField] private Mission5SeesawShadowSystem shadowAreaSystem;

        private Mission5Phase currentPhase;
        private Coroutine introRoutine;
        private SpriteRenderer[] wallRenderers;
        private Color[] wallInitialColors;

        public Mission5Phase CurrentPhase => currentPhase;

        private void Awake()
        {
            currentPhase = initialPhase;
            ResolveReferences();
            CacheWallInitialColors();
            SetFadeAlpha(currentPhase == Mission5Phase.Intro ? 1.0f : 0.0f);
            SetInteractionSystemEnabled(currentPhase == Mission5Phase.Interaction);
        }

        private void Start()
        {
            if (setMission5StateOnStart)
            {
                FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission5);
            }

            if (currentPhase == Mission5Phase.Interaction)
            {
                EnterInteraction();
                return;
            }

            if (currentPhase == Mission5Phase.Outro)
            {
                EnterOutro();
                return;
            }

            EnterIntro();
            if (playOnStart)
            {
                PlayIntro();
            }
        }

        public void PlayIntro()
        {
            if (introRoutine != null)
            {
                StopCoroutine(introRoutine);
            }

            EnterIntro();
            introRoutine = StartCoroutine(PlayIntroRoutine());
        }

        public void EnterIntro()
        {
            currentPhase = Mission5Phase.Intro;
            ResolveReferences();
            RestoreWallInitialColors();
            SetInteractionSystemEnabled(false);
            SetFadeAlpha(1.0f);
        }

        public void EnterInteraction()
        {
            currentPhase = Mission5Phase.Interaction;
            ResolveReferences();
            SetFadeAlpha(0.0f);
            SetInteractionSystemEnabled(true);
        }

        public void EnterOutro()
        {
            currentPhase = Mission5Phase.Outro;
            ResolveReferences();
            SetFadeAlpha(0.0f);
        }

        public IEnumerator PlayOutroRoutine()
        {
            EnterOutro();
            CacheWallInitialColors();

            Transform star = shadowStarTransform;
            float duration = Mathf.Max(0.0f, outroDuration);

            if (duration <= 0.0f)
            {
                SetWallAbsoluteAlpha(outroWallTargetAlpha);
            }
            else
            {
                float wallElapsed = 0.0f;
                while (wallElapsed < duration)
                {
                    wallElapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(wallElapsed / duration);
                    float easedT = Mathf.SmoothStep(0.0f, 1.0f, t);
                    SetWallAbsoluteAlpha(Mathf.Lerp(0.0f, outroWallTargetAlpha, easedT));
                    yield return null;
                }

                SetWallAbsoluteAlpha(outroWallTargetAlpha);
            }

            if (outroJumpDelaySeconds > 0.0f)
            {
                yield return new WaitForSeconds(outroJumpDelaySeconds);
            }

            if (star == null)
            {
                yield break;
            }

            Vector3 starStartPosition = star.position;
            Vector3 starStartScale = star.localScale;
            Vector3 starTargetScale = Vector3.one * outroShadowStarScale;
            float jumpDuration = Mathf.Max(0.0f, outroShadowStarJumpSeconds);

            if (jumpDuration <= 0.0f)
            {
                star.position = outroShadowStarPosition;
                star.localScale = starTargetScale;
                yield break;
            }

            float jumpElapsed = 0.0f;
            while (jumpElapsed < jumpDuration)
            {
                jumpElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(jumpElapsed / jumpDuration);
                float easedT = Mathf.SmoothStep(0.0f, 1.0f, t);
                Vector3 position = Vector3.LerpUnclamped(starStartPosition, outroShadowStarPosition, easedT);
                position.y += Mathf.Sin(t * Mathf.PI) * outroShadowStarJumpHeight;
                star.position = position;
                star.localScale = Vector3.LerpUnclamped(starStartScale, starTargetScale, easedT);
                yield return null;
            }

            star.position = outroShadowStarPosition;
            star.localScale = starTargetScale;
        }

        private IEnumerator PlayIntroRoutine()
        {
            ResolveReferences();
            Transform star = shadowStarTransform;
            Vector3 starStartPosition = star != null ? star.position : Vector3.zero;
            Vector3 starTargetPosition = starStartPosition;
            starTargetPosition.y = introShadowStarTargetY;

            float fadeDuration = Mathf.Max(0.0f, introFadeSeconds);
            float moveDuration = Mathf.Max(0.0f, introShadowStarMoveSeconds);
            float duration = Mathf.Max(fadeDuration, moveDuration);

            if (duration <= 0.0f)
            {
                SetFadeAlpha(0.0f);
                if (star != null)
                {
                    star.position = starTargetPosition;
                }

                yield return PlayIntroNarrationRoutine();
                yield return FadeWallOutRoutine();
                yield return JumpShadowStarToInteractionPositionRoutine();
                EnterInteractionIfNeeded();

                introRoutine = null;
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                if (fadeDuration <= 0.0f)
                {
                    SetFadeAlpha(0.0f);
                }
                else
                {
                    float fadeT = Mathf.Clamp01(elapsed / fadeDuration);
                    float fadeEased = Mathf.SmoothStep(0.0f, 1.0f, fadeT);
                    SetFadeAlpha(Mathf.LerpUnclamped(1.0f, 0.0f, fadeEased));
                }

                if (star != null)
                {
                    if (moveDuration <= 0.0f)
                    {
                        star.position = starTargetPosition;
                    }
                    else
                    {
                        float moveT = Mathf.Clamp01(elapsed / moveDuration);
                        float moveEased = Mathf.SmoothStep(0.0f, 1.0f, moveT);
                        star.position = Vector3.LerpUnclamped(starStartPosition, starTargetPosition, moveEased);
                    }
                }

                yield return null;
            }

            SetFadeAlpha(0.0f);
            if (star != null)
            {
                star.position = starTargetPosition;
            }

            yield return PlayIntroNarrationRoutine();
            yield return FadeWallOutRoutine();
            yield return JumpShadowStarToInteractionPositionRoutine();
            EnterInteractionIfNeeded();

            introRoutine = null;
        }

        private IEnumerator PlayIntroNarrationRoutine()
        {
            ResolveReferences();

            if (introNarrationStartDelaySeconds > 0.0f)
            {
                yield return new WaitForSeconds(introNarrationStartDelaySeconds);
            }

            NarrationSubtitleSequencePlayer narrationPlayer = ResolveIntroNarrationPlayer();
            if (narrationPlayer == null || introNarrationStepCount <= 0)
            {
                yield break;
            }

            yield return narrationPlayer.PlayRangeAndWaitRoutine(
                introNarrationStartStepIndex,
                introNarrationStepCount);
        }

        private void EnterInteractionIfNeeded()
        {
            if (autoEnterInteractionAfterIntro)
            {
                EnterInteraction();
            }
        }

        private void SetInteractionSystemEnabled(bool isEnabled)
        {
            ResolveReferences();

            if (shadowAreaSystem != null)
            {
                shadowAreaSystem.enabled = isEnabled;
            }
        }

        private void SetFadeAlpha(float alpha)
        {
            if (fadeOverlayImage == null)
            {
                return;
            }

            if (fadeOverlayImage.transform.parent != null &&
                !fadeOverlayImage.transform.parent.gameObject.activeSelf)
            {
                fadeOverlayImage.transform.parent.gameObject.SetActive(true);
            }

            Color color = fadeOverlayImage.color;
            color.r = 0.0f;
            color.g = 0.0f;
            color.b = 0.0f;
            color.a = Mathf.Clamp01(alpha);
            fadeOverlayImage.color = color;
            fadeOverlayImage.raycastTarget = false;
            fadeOverlayImage.gameObject.SetActive(color.a > 0.001f);
        }

        private IEnumerator FadeWallOutRoutine()
        {
            CacheWallInitialColors();
            if (wallRenderers == null || wallRenderers.Length == 0)
            {
                yield break;
            }

            Color[] startColors = new Color[wallRenderers.Length];
            for (int i = 0; i < wallRenderers.Length; i++)
            {
                startColors[i] = wallRenderers[i] != null ? wallRenderers[i].color : Color.white;
            }

            float duration = Mathf.Max(0.0f, wallFadeOutSeconds);
            if (duration <= 0.0f)
            {
                SetWallAlpha(startColors, 0.0f);
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetWallAlpha(startColors, 1.0f - t);
                yield return null;
            }

            SetWallAlpha(startColors, 0.0f);
        }

        private void SetWallAlpha(Color[] startColors, float alphaScale)
        {
            if (wallRenderers == null)
            {
                return;
            }

            for (int i = 0; i < wallRenderers.Length; i++)
            {
                SpriteRenderer renderer = wallRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Color color = startColors != null && i < startColors.Length ? startColors[i] : renderer.color;
                color.a *= Mathf.Clamp01(alphaScale);
                renderer.color = color;
            }
        }

        private void SetWallAbsoluteAlpha(float alpha)
        {
            if (wallRenderers == null || wallInitialColors == null)
            {
                CacheWallInitialColors();
            }

            if (wallRenderers == null || wallInitialColors == null)
            {
                return;
            }

            float clampedAlpha = Mathf.Clamp01(alpha);
            int count = Mathf.Min(wallRenderers.Length, wallInitialColors.Length);
            for (int i = 0; i < count; i++)
            {
                SpriteRenderer renderer = wallRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Color color = wallInitialColors[i];
                color.a = clampedAlpha;
                renderer.color = color;
            }
        }

        private void RestoreWallInitialColors()
        {
            CacheWallInitialColors();
            if (wallRenderers == null || wallInitialColors == null)
            {
                return;
            }

            int count = Mathf.Min(wallRenderers.Length, wallInitialColors.Length);
            for (int i = 0; i < count; i++)
            {
                if (wallRenderers[i] != null)
                {
                    wallRenderers[i].color = wallInitialColors[i];
                }
            }
        }

        private IEnumerator JumpShadowStarToInteractionPositionRoutine()
        {
            ResolveReferences();
            Transform star = shadowStarTransform;
            if (star == null)
            {
                yield break;
            }

            Vector3 startPosition = star.position;
            Vector3 targetPosition = interactionShadowStarPosition;
            float duration = Mathf.Max(0.0f, interactionShadowStarJumpSeconds);
            if (duration <= 0.0f)
            {
                star.position = targetPosition;
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = Mathf.SmoothStep(0.0f, 1.0f, t);
                Vector3 position = Vector3.LerpUnclamped(startPosition, targetPosition, easedT);
                position.y += Mathf.Sin(t * Mathf.PI) * interactionShadowStarJumpHeight;
                star.position = position;
                yield return null;
            }

            star.position = targetPosition;
        }

        private void ResolveReferences()
        {
            if (shadowAreaSystem == null)
            {
                shadowAreaSystem = GetComponent<Mission5SeesawShadowSystem>();
            }

            ResolveIntroNarrationPlayer();
        }

        private void CacheWallInitialColors()
        {
            ResolveReferences();
            if (wallRoot == null)
            {
                return;
            }

            if (wallRenderers == null || wallRenderers.Length == 0)
            {
                wallRenderers = wallRoot.GetComponentsInChildren<SpriteRenderer>(true);
            }

            if (wallRenderers == null ||
                wallRenderers.Length == 0 ||
                (wallInitialColors != null && wallInitialColors.Length == wallRenderers.Length))
            {
                return;
            }

            wallInitialColors = new Color[wallRenderers.Length];
            for (int i = 0; i < wallRenderers.Length; i++)
            {
                wallInitialColors[i] = wallRenderers[i] != null ? wallRenderers[i].color : Color.white;
            }
        }

        private NarrationSubtitleSequencePlayer ResolveIntroNarrationPlayer()
        {
            if (introNarrationPlayer == null)
            {
                introNarrationPlayer = GetComponent<NarrationSubtitleSequencePlayer>();
            }

            return introNarrationPlayer;
        }
    }
}
