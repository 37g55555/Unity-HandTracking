using System;
using System.Collections;
using UnityEngine;

namespace ShadowPrototype
{
    public sealed class NarrationSubtitleSequencePlayer : MonoBehaviour
    {
        [Serializable]
        public sealed class NarrationStep
        {
            [SerializeField] private AudioClip audioClip;
            [SerializeField, TextArea] private string subtitle;
            [SerializeField, Min(0.0f)] private float gapAfterSeconds = 1.0f;

            public AudioClip AudioClip => audioClip;
            public string Subtitle => subtitle;
            public float GapAfterSeconds => Mathf.Max(0.0f, gapAfterSeconds);
        }

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private MainSubtitleController subtitleController;
        [SerializeField] private NarrationStep[] narrationChain = Array.Empty<NarrationStep>();

        public int StepCount => narrationChain != null ? narrationChain.Length : 0;

        private void OnDisable()
        {
            StopPlayback();
        }

        public IEnumerator PlayAndWaitRoutine(
            Action<int, float, float, float> handleClipProgress = null,
            Action<int, float, float, float> handleGapProgress = null)
        {
            yield return PlayRangeAndWaitRoutine(0, StepCount, handleClipProgress, handleGapProgress);
        }

        public IEnumerator PlayRangeAndWaitRoutine(
            int firstStepIndex,
            int stepCount,
            Action<int, float, float, float> handleClipProgress = null,
            Action<int, float, float, float> handleGapProgress = null)
        {
            NarrationStep[] steps = narrationChain ?? Array.Empty<NarrationStep>();
            if (steps.Length == 0 || stepCount <= 0)
            {
                yield break;
            }

            AudioSource resolvedAudioSource = ResolveAudioSource();
            int startIndex = Mathf.Clamp(firstStepIndex, 0, steps.Length);
            int endIndex = Mathf.Clamp(startIndex + stepCount, startIndex, steps.Length);

            for (int i = startIndex; i < endIndex; i++)
            {
                NarrationStep step = steps[i];
                if (step == null)
                {
                    continue;
                }

                yield return PlayStepRoutine(step, i, resolvedAudioSource, handleClipProgress, handleGapProgress);
            }
        }

        public float CalculateDurationThroughStep(int lastStepIndex)
        {
            NarrationStep[] steps = narrationChain ?? Array.Empty<NarrationStep>();
            if (steps.Length == 0 || lastStepIndex < 0)
            {
                return 0.0f;
            }

            AudioSource resolvedAudioSource = ResolveAudioSource();
            int clampedLastStepIndex = Mathf.Min(lastStepIndex, steps.Length - 1);
            float duration = 0.0f;

            for (int i = 0; i <= clampedLastStepIndex; i++)
            {
                NarrationStep step = steps[i];
                if (step == null)
                {
                    continue;
                }

                duration += GetAudioClipDuration(step.AudioClip, resolvedAudioSource);
                if (i < clampedLastStepIndex)
                {
                    duration += step.GapAfterSeconds;
                }
            }

            return Mathf.Max(0.0f, duration);
        }

        public void StopPlayback()
        {
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            HideSubtitle();
        }

        private IEnumerator PlayStepRoutine(
            NarrationStep step,
            int stepIndex,
            AudioSource resolvedAudioSource,
            Action<int, float, float, float> handleClipProgress,
            Action<int, float, float, float> handleGapProgress)
        {
            AudioClip clip = step.AudioClip;
            float clipDuration = GetAudioClipDuration(clip, resolvedAudioSource);

            if (clip != null)
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                }

                if (resolvedAudioSource != null)
                {
                    resolvedAudioSource.Stop();
                    resolvedAudioSource.clip = clip;
                    resolvedAudioSource.Play();
                }

                ShowSubtitle(step.Subtitle);
            }

            float clipElapsed = 0.0f;
            while (clipElapsed < clipDuration)
            {
                float deltaTime = Time.unscaledDeltaTime;
                clipElapsed += deltaTime;
                handleClipProgress?.Invoke(stepIndex, Mathf.Min(clipElapsed, clipDuration), clipDuration, deltaTime);
                yield return null;
            }

            if (clip != null)
            {
                HideSubtitle();
            }

            float gapElapsed = 0.0f;
            float gapDuration = step.GapAfterSeconds;
            while (gapElapsed < gapDuration)
            {
                float deltaTime = Time.unscaledDeltaTime;
                gapElapsed += deltaTime;
                handleGapProgress?.Invoke(stepIndex, Mathf.Min(gapElapsed, gapDuration), gapDuration, deltaTime);
                yield return null;
            }
        }

        private AudioSource ResolveAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0.0f;
            return audioSource;
        }

        private MainSubtitleController ResolveSubtitleController()
        {
            if (subtitleController == null)
            {
                subtitleController = GetComponent<MainSubtitleController>();
            }

            if (subtitleController == null)
            {
                subtitleController = FindObjectOfType<MainSubtitleController>();
            }

            return subtitleController;
        }

        private void ShowSubtitle(string message)
        {
            MainSubtitleController resolvedSubtitleController = ResolveSubtitleController();
            if (resolvedSubtitleController == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            resolvedSubtitleController.ShowMessage(message);
        }

        private void HideSubtitle()
        {
            MainSubtitleController resolvedSubtitleController = ResolveSubtitleController();
            if (resolvedSubtitleController != null)
            {
                resolvedSubtitleController.HideMessage();
            }
        }

        private static float GetAudioClipDuration(AudioClip clip, AudioSource resolvedAudioSource)
        {
            if (clip == null)
            {
                return 0.0f;
            }

            float pitch = resolvedAudioSource != null ? Mathf.Abs(resolvedAudioSource.pitch) : 1.0f;
            return clip.length / Mathf.Max(0.01f, pitch);
        }
    }
}
