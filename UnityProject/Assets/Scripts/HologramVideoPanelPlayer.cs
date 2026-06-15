using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ShadowPrototype
{
    [DisallowMultipleComponent]
    public sealed class HologramVideoPanelPlayer : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private RenderTexture targetTexture;
        [SerializeField] private RawImage[] outputPanels = Array.Empty<RawImage>();
        [SerializeField] private string videoRelativePath = "HologramVideos/starChar_1_1_tts.mp4";
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool loop;
        [SerializeField, Range(0.0f, 1.0f)] private float audioVolume = 1.0f;
        [SerializeField] private bool clearRenderTextureOnStart = true;
        [SerializeField] private MonoBehaviour[] interactionBehavioursToEnableAfterVideo = Array.Empty<MonoBehaviour>();
        [SerializeField] private bool hidePanelsWhenNotPlaying = true;

        [Header("Post Video Narration")]
        [SerializeField] private AudioSource postVideoNarrationAudioSource;
        [SerializeField] private AudioClip postVideoNarrationClip;
        [SerializeField, Min(0.0f)] private float postVideoNarrationDelaySeconds = 1.0f;
        [SerializeField] private bool playNarrationBeforeFirstInteraction = true;

        private bool isVideoOutputActive;
        private bool hasPlayedPostVideoNarration;
        private Coroutine enableInteractionRoutine;

        private void Awake()
        {
            ResolveReferences();
            SetInteractionBehavioursEnabled(false);
            ConfigureOutputPanels(false);
            ConfigureVideoPlayer();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleVideoLoopPointReached;
                videoPlayer.loopPointReached += HandleVideoLoopPointReached;
            }
        }

        private void OnDisable()
        {
            StopEnableInteractionRoutine();

            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleVideoLoopPointReached;
            }
        }

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        public void Play()
        {
            Play(videoRelativePath);
        }

        public void Play(string nextVideoRelativePath)
        {
            ResolveReferences();
            StopEnableInteractionRoutine();
            SetInteractionBehavioursEnabled(false);
            if (!string.IsNullOrWhiteSpace(nextVideoRelativePath))
            {
                videoRelativePath = nextVideoRelativePath;
            }

            ConfigureOutputPanels(true);
            ConfigureVideoPlayer();
            isVideoOutputActive = true;
            videoPlayer.Play();
        }

        private void ResolveReferences()
        {
            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
                if (videoPlayer == null)
                {
                    videoPlayer = gameObject.AddComponent<VideoPlayer>();
                }
            }

            if (outputPanels == null || outputPanels.Length == 0)
            {
                outputPanels = new[]
                {
                    FindPanelImage("Video_Front"),
                    FindPanelImage("Video_Left"),
                    FindPanelImage("Video_Right")
                };
            }

            if (postVideoNarrationAudioSource == null)
            {
                postVideoNarrationAudioSource = GetComponent<AudioSource>();
            }
        }

        private void ConfigureOutputPanels(bool visible)
        {
            if (outputPanels == null)
            {
                return;
            }

            for (int i = 0; i < outputPanels.Length; i++)
            {
                RawImage panel = outputPanels[i];
                if (panel == null)
                {
                    continue;
                }

                if (targetTexture != null)
                {
                    panel.texture = targetTexture;
                }

                panel.color = Color.white;
                panel.raycastTarget = false;
                if (hidePanelsWhenNotPlaying)
                {
                    panel.gameObject.SetActive(visible);
                }
            }

            isVideoOutputActive = visible;
        }

        private void HideOutputPanels()
        {
            ConfigureOutputPanels(false);
        }

        private void ConfigureVideoPlayer()
        {
            if (videoPlayer == null || targetTexture == null)
            {
                return;
            }

            if (clearRenderTextureOnStart)
            {
                ClearRenderTexture(targetTexture);
            }

            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.isLooping = loop;
            videoPlayer.skipOnDrop = true;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = targetTexture;
            videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = ResolveVideoUrl();
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.controlledAudioTrackCount = 1;
            videoPlayer.EnableAudioTrack(0, audioVolume > 0.0f);
            videoPlayer.SetDirectAudioMute(0, audioVolume <= 0.0f);
            videoPlayer.SetDirectAudioVolume(0, audioVolume);
        }

        private void HandleVideoLoopPointReached(VideoPlayer source)
        {
            if (loop || !isVideoOutputActive)
            {
                return;
            }

            source.Stop();
            HideOutputPanels();
            EnableInteractionAfterVideo();
        }

        private void EnableInteractionAfterVideo()
        {
            if (ShouldPlayPostVideoNarration())
            {
                hasPlayedPostVideoNarration = true;
                enableInteractionRoutine = StartCoroutine(EnableInteractionAfterNarrationRoutine());
                return;
            }

            SetInteractionBehavioursEnabled(true);
        }

        private bool ShouldPlayPostVideoNarration()
        {
            return playNarrationBeforeFirstInteraction &&
                !hasPlayedPostVideoNarration &&
                postVideoNarrationClip != null;
        }

        private IEnumerator EnableInteractionAfterNarrationRoutine()
        {
            if (postVideoNarrationDelaySeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(postVideoNarrationDelaySeconds);
            }

            ResolveReferences();
            postVideoNarrationAudioSource = HologramAudioPlaybackUtility.Resolve2DAudioSource(
                this,
                postVideoNarrationAudioSource);
            float narrationDuration = GetAudioClipDuration(
                postVideoNarrationClip,
                postVideoNarrationAudioSource);

            if (postVideoNarrationClip != null && postVideoNarrationAudioSource != null)
            {
                yield return LoadAudioClipRoutine(postVideoNarrationClip);

                postVideoNarrationAudioSource.Stop();
                postVideoNarrationAudioSource.PlayOneShot(postVideoNarrationClip, 1.0f);
            }

            if (narrationDuration > 0.0f)
            {
                yield return new WaitForSecondsRealtime(narrationDuration);
            }

            SetInteractionBehavioursEnabled(true);
            enableInteractionRoutine = null;
        }

        private static IEnumerator LoadAudioClipRoutine(AudioClip clip)
        {
            if (clip == null)
            {
                yield break;
            }

            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }

            while (clip.loadState == AudioDataLoadState.Loading)
            {
                yield return null;
            }
        }

        private void StopEnableInteractionRoutine()
        {
            if (enableInteractionRoutine == null)
            {
                return;
            }

            StopCoroutine(enableInteractionRoutine);
            enableInteractionRoutine = null;
        }

        private void SetInteractionBehavioursEnabled(bool isEnabled)
        {
            if (interactionBehavioursToEnableAfterVideo == null)
            {
                return;
            }

            for (int i = 0; i < interactionBehavioursToEnableAfterVideo.Length; i++)
            {
                MonoBehaviour behaviour = interactionBehavioursToEnableAfterVideo[i];
                if (behaviour != null)
                {
                    behaviour.enabled = isEnabled;
                }
            }
        }

        private RawImage FindPanelImage(string panelName)
        {
            RawImage[] images = GetComponentsInChildren<RawImage>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].name == panelName)
                {
                    return images[i];
                }
            }

            return null;
        }

        private static float GetAudioClipDuration(AudioClip clip, AudioSource audioSource)
        {
            if (clip == null)
            {
                return 0.0f;
            }

            float pitch = audioSource != null ? Mathf.Abs(audioSource.pitch) : 1.0f;
            if (pitch <= 0.001f)
            {
                pitch = 1.0f;
            }

            return clip.length / pitch;
        }

        private string ResolveVideoUrl()
        {
            if (string.IsNullOrWhiteSpace(videoRelativePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(videoRelativePath))
            {
                return videoRelativePath.Replace('\\', '/');
            }

            string videoPath = Path.Combine(Application.streamingAssetsPath, videoRelativePath);
            if (File.Exists(videoPath))
            {
                return videoPath.Replace('\\', '/');
            }

            string folder = Path.GetDirectoryName(videoPath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(videoRelativePath);
                string searchPattern = string.IsNullOrWhiteSpace(fileNameWithoutExtension)
                    ? "*.mp4"
                    : $"{fileNameWithoutExtension}*.mp4";
                string[] matches = Directory.GetFiles(folder, searchPattern);
                if (matches.Length > 0)
                {
                    return matches[0].Replace('\\', '/');
                }
            }

            Debug.LogWarning($"HologramVideoPanelPlayer: video file was not found: {videoPath}");
            return videoPath.Replace('\\', '/');
        }

        private static void ClearRenderTexture(RenderTexture renderTexture)
        {
            if (!renderTexture.IsCreated())
            {
                renderTexture.Create();
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previousActive;
        }
    }
}
