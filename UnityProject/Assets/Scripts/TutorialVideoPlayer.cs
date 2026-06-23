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
