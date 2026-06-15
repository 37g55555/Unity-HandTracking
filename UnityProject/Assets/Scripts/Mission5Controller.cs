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
            Interaction
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

        [Header("Interaction")]
        [SerializeField] private Mission5SeesawShadowSystem shadowAreaSystem;

        private Mission5Phase currentPhase;
        private Coroutine introRoutine;

        public Mission5Phase CurrentPhase => currentPhase;

        private void Awake()
        {
            currentPhase = initialPhase;
            ResolveReferences();
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

        private void ResolveReferences()
        {
            if (shadowAreaSystem == null)
            {
                shadowAreaSystem = GetComponent<Mission5SeesawShadowSystem>();
                if (shadowAreaSystem == null)
                {
                    shadowAreaSystem = FindObjectOfType<Mission5SeesawShadowSystem>(true);
                }
            }

            if (shadowStarTransform == null)
            {
                GameObject shadowStarObject = GameObject.Find("ShadowStar");
                shadowStarTransform = shadowStarObject != null ? shadowStarObject.transform : null;
            }

            if (fadeOverlayImage == null)
            {
                GameObject fadeObject = GameObject.Find("Mission5IntroFadeImage");
                fadeOverlayImage = fadeObject != null ? fadeObject.GetComponent<Image>() : null;
            }

            ResolveIntroNarrationPlayer();
        }

        private NarrationSubtitleSequencePlayer ResolveIntroNarrationPlayer()
        {
            if (introNarrationPlayer == null)
            {
                introNarrationPlayer = GetComponent<NarrationSubtitleSequencePlayer>();
            }

            if (introNarrationPlayer == null)
            {
                introNarrationPlayer = FindObjectOfType<NarrationSubtitleSequencePlayer>();
            }

            return introNarrationPlayer;
        }
    }
}
