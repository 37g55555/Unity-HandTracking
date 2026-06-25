using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ShadowPrototype
{
    [DisallowMultipleComponent]
    public sealed class TutorialVideoPlayer : MonoBehaviour
    {
        private static readonly Vector2Int DefaultRenderTextureSize = new Vector2Int(1280, 720);

        [SerializeField] private VideoClip videoClip;
        [SerializeField] private RawImage outputImage;
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private Vector2Int renderTextureSize = DefaultRenderTextureSize;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool playOnEnable = true;

        private RenderTexture renderTexture;

        public VideoClip CurrentClip => videoClip;

        private void Awake()
        {
            Configure();
        }

        private void OnEnable()
        {
            Configure();

            if (playOnEnable && videoPlayer != null && videoClip != null)
            {
                videoPlayer.time = 0.0;
                videoPlayer.Play();
            }
        }

        private void OnDisable()
        {
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
            }
        }

        private void OnDestroy()
        {
            if (videoPlayer != null && renderTexture != null && videoPlayer.targetTexture == renderTexture)
            {
                videoPlayer.targetTexture = null;
            }

            if (outputImage != null && outputImage.texture == renderTexture)
            {
                outputImage.texture = null;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }

        public IEnumerator PlayClipAndWaitRoutine(VideoClip clip)
        {
            if (clip == null)
            {
                yield break;
            }

            VideoClip previousClip = videoClip;
            bool previousLoop = loop;
            videoClip = clip;
            loop = false;
            Configure();

            if (videoPlayer == null || videoClip == null)
            {
                RestoreClip(previousClip, previousLoop);
                yield break;
            }

            bool completed = false;
            VideoPlayer.EventHandler handleLoopPointReached = _source => completed = true;

            videoPlayer.loopPointReached += handleLoopPointReached;
            videoPlayer.Stop();
            videoPlayer.time = 0.0;
            videoPlayer.Prepare();

            float prepareElapsed = 0.0f;
            while (!videoPlayer.isPrepared && prepareElapsed < 5.0f)
            {
                prepareElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            videoPlayer.Play();

            float playbackElapsed = 0.0f;
            float clipLengthSeconds = videoClip.length > 0.0 ? (float)videoClip.length : 0.0f;
            float maxPlaybackSeconds = clipLengthSeconds > 0.0f ? clipLengthSeconds + 2.0f : 60.0f;
            while (!completed && playbackElapsed < maxPlaybackSeconds)
            {
                playbackElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            videoPlayer.loopPointReached -= handleLoopPointReached;
            videoPlayer.Stop();
            RestoreClip(previousClip, previousLoop);
        }

        public void PlayClipLooping(VideoClip clip)
        {
            if (clip == null)
            {
                return;
            }

            videoClip = clip;
            loop = true;
            Configure();

            if (videoPlayer == null || videoClip == null)
            {
                return;
            }

            videoPlayer.Stop();
            videoPlayer.time = 0.0;
            videoPlayer.Play();
        }

        private void Configure()
        {
            if (outputImage == null)
            {
                outputImage = GetComponent<RawImage>();
            }

            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
            }

            if (outputImage == null || videoPlayer == null)
            {
                return;
            }

            EnsureRenderTexture();

            outputImage.texture = renderTexture;
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.isLooping = loop;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = videoClip;
        }

        private void RestoreClip(VideoClip clip, bool shouldLoop)
        {
            videoClip = clip;
            loop = shouldLoop;
            Configure();
        }

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(16, renderTextureSize.x);
            int height = Mathf.Max(16, renderTextureSize.y);

            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height)
            {
                return;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "TutorialVideoRenderTexture"
            };
        }
    }
}
