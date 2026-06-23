using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public sealed class Mission4Controller : MonoBehaviour
    {
        public enum Mission4Phase
        {
            Intro,
            Interaction
        }

        [Header("Phase")]
        [SerializeField] private Mission4Phase initialPhase = Mission4Phase.Intro;
        [SerializeField] private bool autoEnterInteractionAfterIntro = true;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool setMission4StateOnStart = true;

        [Header("Narration")]
        [SerializeField] private NarrationSubtitleSequencePlayer introNarrationPlayer;
        [SerializeField, Min(0)] private int introNarrationStartStepIndex;
        [SerializeField, Min(0)] private int introNarrationStepCount = 3;

        [Header("Interaction Systems")]
        [SerializeField] private Mission4ArucoCameraSystem arucoCameraSystem;
        [SerializeField] private ArucoMarkerFollower markerFollower;
        [SerializeField] private Mission4ShadowStarLightFollower shadowStarLightFollower;
        [SerializeField] private Mission4DoorTransition doorTransition;

        [Header("Interaction UI")]
        [SerializeField] private GameObject interactionInstructionObject;
        [SerializeField] private Text interactionInstructionTextComponent;
        [SerializeField] private string interactionInstructionText = "\uBE5B\uC774 \uD544\uC694\uD560\uC9C0\uB3C4 \uBAA8\uB978\uB2E4.";
        [SerializeField] private Color interactionInstructionTextColor = Color.white;
        [SerializeField, Min(12)] private int interactionInstructionFontSize = 36;

        [Header("Completion")]
        [SerializeField] private AudioSource completionAudioSource;
        [SerializeField] private AudioClip completionNarrationClip;
        [SerializeField, Range(0.0f, 1.0f)] private float completionDingVolume = 0.85f;
        [SerializeField, Min(0.0f)] private float completionNarrationDelaySeconds = 1.0f;
        [SerializeField] private string nextSceneName = "Mission5";

        private Mission4Phase currentPhase;
        private Coroutine introRoutine;
        private Coroutine completionRoutine;
        private bool completionStarted;
        private AudioClip completionDingClip;

        public Mission4Phase CurrentPhase => currentPhase;

        private void Awake()
        {
            currentPhase = initialPhase;
            ResolveReferences();
            SetInteractionSystemsEnabled(currentPhase == Mission4Phase.Interaction);
            SetInteractionInstructionVisible(currentPhase == Mission4Phase.Interaction);
        }

        private void Start()
        {
            if (setMission4StateOnStart)
            {
                FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission4);
            }

            if (currentPhase == Mission4Phase.Interaction)
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
            currentPhase = Mission4Phase.Intro;
            SetInteractionSystemsEnabled(false);
            SetInteractionInstructionVisible(false);
        }

        public void EnterInteraction()
        {
            currentPhase = Mission4Phase.Interaction;
            SetInteractionSystemsEnabled(true);
            SetInteractionInstructionVisible(true);
        }

        public void HandleDoorReached()
        {
            if (completionStarted)
            {
                return;
            }

            if (completionRoutine != null)
            {
                StopCoroutine(completionRoutine);
            }

            completionRoutine = StartCoroutine(CompleteMissionRoutine());
        }

        public void DebugAdvance()
        {
            if (currentPhase == Mission4Phase.Intro)
            {
                if (introRoutine != null)
                {
                    StopCoroutine(introRoutine);
                    introRoutine = null;
                }

                EnterInteraction();
                return;
            }

            HandleDoorReached();
        }

        private IEnumerator PlayIntroRoutine()
        {
            NarrationSubtitleSequencePlayer narrationPlayer = ResolveNarrationPlayer();
            if (narrationPlayer != null && narrationPlayer.StepCount > 0)
            {
                int startIndex = Mathf.Clamp(introNarrationStartStepIndex, 0, narrationPlayer.StepCount);
                int stepCount = Mathf.Clamp(introNarrationStepCount, 0, narrationPlayer.StepCount - startIndex);
                if (stepCount > 0)
                {
                    yield return narrationPlayer.PlayRangeAndWaitRoutine(startIndex, stepCount);
                }
            }

            if (autoEnterInteractionAfterIntro)
            {
                EnterInteraction();
            }

            introRoutine = null;
        }

        private IEnumerator CompleteMissionRoutine()
        {
            completionStarted = true;
            SetInteractionSystemsEnabled(false);
            yield return PlayCompletionDingRoutine();
            SetInteractionInstructionVisible(false);

            if (completionNarrationDelaySeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(completionNarrationDelaySeconds);
            }

            yield return PlayCompletionNarrationRoutine();

            FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission5);
            if (!string.IsNullOrWhiteSpace(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
            }

            completionRoutine = null;
        }

        private IEnumerator PlayCompletionDingRoutine()
        {
            if (completionDingVolume <= 0.0f)
            {
                yield break;
            }

            ResolveReferences();
            if (completionAudioSource == null)
            {
                yield break;
            }

            completionAudioSource.playOnAwake = false;
            completionAudioSource.loop = false;
            completionAudioSource.spatialBlend = 0.0f;

            if (completionDingClip == null)
            {
                completionDingClip = CreateCompletionDingClip();
            }

            completionAudioSource.PlayOneShot(completionDingClip, completionDingVolume);

            float elapsed = 0.0f;
            float duration = completionDingClip != null ? completionDingClip.length : 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator PlayCompletionNarrationRoutine()
        {
            ResolveReferences();
            if (completionNarrationClip == null || completionAudioSource == null)
            {
                yield break;
            }

            if (completionNarrationClip.loadState == AudioDataLoadState.Unloaded)
            {
                completionNarrationClip.LoadAudioData();
            }

            while (completionNarrationClip.loadState == AudioDataLoadState.Loading)
            {
                yield return null;
            }

            completionAudioSource.playOnAwake = false;
            completionAudioSource.loop = false;
            completionAudioSource.spatialBlend = 0.0f;
            completionAudioSource.Stop();
            completionAudioSource.clip = completionNarrationClip;
            completionAudioSource.Play();

            float duration = GetAudioClipDuration(completionNarrationClip, completionAudioSource);
            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void SetInteractionSystemsEnabled(bool isEnabled)
        {
            ResolveReferences();

            if (markerFollower != null)
            {
                markerFollower.enabled = isEnabled;
            }

            if (shadowStarLightFollower != null)
            {
                shadowStarLightFollower.enabled = isEnabled;
            }

            if (doorTransition != null)
            {
                doorTransition.enabled = isEnabled;
            }

            if (arucoCameraSystem == null)
            {
                return;
            }

            if (isEnabled)
            {
                arucoCameraSystem.enabled = true;
                arucoCameraSystem.BeginTracking();
            }
            else
            {
                arucoCameraSystem.StopTracking();
                arucoCameraSystem.enabled = false;
            }
        }

        private void SetInteractionInstructionVisible(bool isVisible)
        {
            ResolveInstructionReferences();

            if (interactionInstructionTextComponent != null)
            {
                interactionInstructionTextComponent.text = interactionInstructionText;
                interactionInstructionTextComponent.color = interactionInstructionTextColor;
                interactionInstructionTextComponent.fontSize = Mathf.Max(12, interactionInstructionFontSize);
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

        private void ResolveInstructionReferences()
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

        private NarrationSubtitleSequencePlayer ResolveNarrationPlayer()
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

        private void ResolveReferences()
        {
            ResolveNarrationPlayer();

            if (arucoCameraSystem == null)
            {
                arucoCameraSystem = GetComponent<Mission4ArucoCameraSystem>();
            }

            if (markerFollower == null)
            {
                markerFollower = FindObjectOfType<ArucoMarkerFollower>();
            }

            if (shadowStarLightFollower == null)
            {
                shadowStarLightFollower = FindObjectOfType<Mission4ShadowStarLightFollower>();
            }

            if (doorTransition == null)
            {
                doorTransition = FindObjectOfType<Mission4DoorTransition>();
            }

            if (completionAudioSource == null)
            {
                completionAudioSource = GetComponent<AudioSource>();
            }

            ResolveInstructionReferences();
        }

        private static float GetAudioClipDuration(AudioClip clip, AudioSource audioSource)
        {
            if (clip == null)
            {
                return 0.0f;
            }

            float pitch = audioSource != null ? Mathf.Abs(audioSource.pitch) : 1.0f;
            return clip.length / Mathf.Max(0.01f, pitch);
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

            AudioClip clip = AudioClip.Create("Mission4CompletionDing", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
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
