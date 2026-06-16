using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ShadowPrototype
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FullscreenStreamingVideoPlayer : MonoBehaviour
    {
        private static readonly Vector2Int DefaultRenderTextureSize = new Vector2Int(1920, 1080);

        [SerializeField] private string videoRelativePath = "Videos/6 Metamorphosis.mp4";
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool loop;
        [SerializeField] private bool hideWhenComplete;
        [SerializeField] private bool previewInEditMode = true;
        [SerializeField] private Color editorPreviewColor = new Color(1.0f, 1.0f, 1.0f, 0.35f);
        [SerializeField, Range(0.0f, 1.0f)] private float audioVolume = 1.0f;
        [SerializeField] private int sortingOrder = 4000;
        [SerializeField] private Color backgroundColor = Color.black;
        [SerializeField, Min(0.1f)] private float prepareTimeoutSeconds = 15.0f;
        [SerializeField, Min(0.1f)] private float playbackStallTimeoutSeconds = 15.0f;
        [SerializeField] private Vector2Int renderTextureSize = DefaultRenderTextureSize;

        private Canvas canvas;
        private RawImage videoImage;
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;
        private Coroutine playbackRoutine;
        private bool playbackCompleted;

        private void Awake()
        {
            EnsureOverlay();
            EnsureVideoPlayer();

            if (Application.isPlaying)
            {
                ConfigureVideoPlayer();
                SetVisible(false);
            }
            else
            {
                RefreshEditorPreview();
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                RefreshEditorPreview();
            }
        }

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                RefreshEditorPreview();
            }
        }

        public void Play()
        {
            if (playbackRoutine != null)
            {
                StopCoroutine(playbackRoutine);
            }

            playbackRoutine = StartCoroutine(PlayRoutine());
        }

        public IEnumerator PlayAndWaitRoutine()
        {
            if (playbackRoutine != null)
            {
                StopCoroutine(playbackRoutine);
                playbackRoutine = null;
            }

            yield return PlayRoutine();
        }

        public void SkipPlayback()
        {
            playbackCompleted = true;
            if (playbackRoutine != null)
            {
                StopCoroutine(playbackRoutine);
                playbackRoutine = null;
            }

            StopPlayback(true);
            playbackCompleted = true;
        }

        private IEnumerator PlayRoutine()
        {
            if (!ConfigureVideoPlayer())
            {
                playbackRoutine = null;
                yield break;
            }

            playbackCompleted = false;
            videoPlayer.loopPointReached -= HandleLoopPointReached;
            videoPlayer.loopPointReached += HandleLoopPointReached;
            SetVisible(true);

            videoPlayer.Prepare();
            float prepareDeadline = Time.realtimeSinceStartup + prepareTimeoutSeconds;
            while (!videoPlayer.isPrepared && Time.realtimeSinceStartup < prepareDeadline)
            {
                yield return null;
            }

            if (!videoPlayer.isPrepared)
            {
                Debug.LogWarning($"FullscreenStreamingVideoPlayer: prepare timed out: {videoPlayer.url}");
                StopPlayback(true);
                playbackRoutine = null;
                yield break;
            }

            videoPlayer.Play();
            float playbackStartTime = Time.realtimeSinceStartup;
            float lastFrameProgressTime = Time.realtimeSinceStartup;
            long lastFrame = videoPlayer.frame;
            while (!playbackCompleted && videoPlayer != null)
            {
                long currentFrame = videoPlayer.frame;
                if (currentFrame != lastFrame)
                {
                    lastFrame = currentFrame;
                    lastFrameProgressTime = Time.realtimeSinceStartup;
                }

                if (HasPlaybackEnded(currentFrame, playbackStartTime))
                {
                    break;
                }

                if (Time.realtimeSinceStartup - lastFrameProgressTime >= playbackStallTimeoutSeconds)
                {
                    Debug.LogWarning($"FullscreenStreamingVideoPlayer: playback stalled: {videoPlayer.url}");
                    break;
                }

                yield return null;
            }

            if (hideWhenComplete)
            {
                StopPlayback(true);
            }
            else if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleLoopPointReached;
            }

            playbackRoutine = null;
        }

        private bool HasPlaybackEnded(long currentFrame, float playbackStartTime)
        {
            if (playbackCompleted)
            {
                return true;
            }

            if (videoPlayer == null)
            {
                return false;
            }

            if (Time.realtimeSinceStartup - playbackStartTime < 0.25f)
            {
                return false;
            }

            double length = videoPlayer.length;
            if (length > 0.0 &&
                !double.IsNaN(length) &&
                videoPlayer.time >= Mathf.Max(0.0f, (float)length - 0.15f))
            {
                return true;
            }

            ulong frameCount = videoPlayer.frameCount;
            if (frameCount > 0 && currentFrame >= (long)frameCount - 2)
            {
                return true;
            }

            if (videoPlayer.isPlaying)
            {
                return false;
            }

            return currentFrame >= 0;
        }

        private bool ConfigureVideoPlayer()
        {
            EnsureOverlay();
            EnsureVideoPlayer();
            EnsureRenderTexture();

            string videoUrl = ResolveVideoUrl();
            if (string.IsNullOrWhiteSpace(videoUrl) || !File.Exists(videoUrl))
            {
                Debug.LogWarning($"FullscreenStreamingVideoPlayer: video file was not found: {videoUrl}");
                SetVisible(false);
                return false;
            }

            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.isLooping = loop;
            videoPlayer.skipOnDrop = true;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = videoUrl.Replace('\\', '/');
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.controlledAudioTrackCount = 1;
            videoPlayer.EnableAudioTrack(0, audioVolume > 0.0f);
            videoPlayer.SetDirectAudioMute(0, audioVolume <= 0.0f);
            videoPlayer.SetDirectAudioVolume(0, audioVolume);

            videoImage.texture = renderTexture;
            videoImage.color = Color.white;
            return true;
        }

        private void RefreshEditorPreview()
        {
            EnsureOverlay();
            EnsureVideoPlayer();

            if (videoImage != null)
            {
                videoImage.texture = null;
                videoImage.color = editorPreviewColor;
            }

            SetVisible(previewInEditMode);
        }

        private void EnsureOverlay()
        {
            if (canvas != null && videoImage != null)
            {
                return;
            }

            Transform existingCanvasTransform = transform.Find("FullscreenVideoCanvas");
            GameObject canvasObject = existingCanvasTransform != null
                ? existingCanvasTransform.gameObject
                : new GameObject("FullscreenVideoCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = canvasObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvasObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DefaultRenderTextureSize.x, DefaultRenderTextureSize.y);
            scaler.matchWidthOrHeight = 0.5f;

            Transform existingImageTransform = canvasObject.transform.Find("FullscreenVideoImage");
            bool createdImage = existingImageTransform == null;
            GameObject imageObject = existingImageTransform != null
                ? existingImageTransform.gameObject
                : new GameObject("FullscreenVideoImage", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);

            videoImage = imageObject.GetComponent<RawImage>();
            if (videoImage == null)
            {
                videoImage = imageObject.AddComponent<RawImage>();
            }

            videoImage.raycastTarget = false;
            videoImage.color = backgroundColor;

            if (createdImage)
            {
                RectTransform imageRect = videoImage.rectTransform;
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.offsetMin = Vector2.zero;
                imageRect.offsetMax = Vector2.zero;
            }
        }

        private void EnsureVideoPlayer()
        {
            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
            }

            if (videoPlayer == null)
            {
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }
        }

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(16, renderTextureSize.x);
            int height = Mathf.Max(16, renderTextureSize.y);
            if (renderTexture != null &&
                renderTexture.width == width &&
                renderTexture.height == height)
            {
                return;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyRenderTexture(renderTexture);
            }

            renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "FullscreenVideoRenderTexture"
            };
            renderTexture.Create();
        }

        private string ResolveVideoUrl()
        {
            if (string.IsNullOrWhiteSpace(videoRelativePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(videoRelativePath))
            {
                return Path.GetFullPath(videoRelativePath);
            }

            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, videoRelativePath));
        }

        private void HandleLoopPointReached(VideoPlayer _source)
        {
            playbackCompleted = true;
        }

        private void StopPlayback(bool hideOutput)
        {
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleLoopPointReached;
                videoPlayer.Stop();
            }

            playbackCompleted = false;
            if (hideOutput)
            {
                SetVisible(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }
        }

        private void OnDisable()
        {
            StopPlayback(true);
        }

        private void OnDestroy()
        {
            StopPlayback(true);

            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyRenderTexture(renderTexture);
                renderTexture = null;
            }
        }

        private static void DestroyRenderTexture(RenderTexture texture)
        {
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }
    }
}
