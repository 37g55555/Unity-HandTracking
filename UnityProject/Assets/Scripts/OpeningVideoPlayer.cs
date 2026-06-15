using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ShadowPrototype
{
    public class OpeningVideoPlayer : MonoBehaviour
    {
        private const string OpeningCanvasName = "OpeningVideoCanvas";
        private const string OpeningImageName = "OpeningVideoImage";
        private static readonly Vector2Int DefaultRenderTextureSize = new Vector2Int(1920, 1080);

        [SerializeField] private VideoClip openingVideoClip;
        [SerializeField] private string openingVideoPath = "opening.mp4";
        [SerializeField] private int sortingOrder = 3000;
        [SerializeField] private Color backgroundColor = Color.black;
        [SerializeField] private bool continueWhenVideoMissing = true;
        [SerializeField, Min(0.1f)] private float prepareTimeoutSeconds = 15.0f;
        [SerializeField, Min(0.1f)] private float playbackStallTimeoutSeconds = 15.0f;
        [SerializeField] private Vector2Int renderTextureSize = DefaultRenderTextureSize;

        private Canvas canvas;
        private RawImage videoImage;
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;
        private bool playbackCompleted;

        public IEnumerator PlayOpeningRoutine()
        {
            if (!TryConfigurePlayback())
            {
                if (continueWhenVideoMissing)
                {
                    yield break;
                }

                Debug.LogWarning("OpeningVideoPlayer: opening video is missing; waiting in Opening state.");
                while (enabled)
                {
                    yield return null;
                }

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
                Debug.LogWarning("OpeningVideoPlayer: opening video prepare timed out; continuing pipeline.");
                StopPlayback();
                yield break;
            }

            videoPlayer.Play();
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

                if (!videoPlayer.isPlaying && currentFrame > 0)
                {
                    break;
                }

                if (Time.realtimeSinceStartup - lastFrameProgressTime >= playbackStallTimeoutSeconds)
                {
                    Debug.LogWarning("OpeningVideoPlayer: opening video playback stalled; continuing pipeline.");
                    break;
                }

                yield return null;
            }

            StopPlayback();
        }

        private bool TryConfigurePlayback()
        {
            if (!EnsureOverlay())
            {
                return false;
            }

            EnsureVideoPlayer();
            EnsureRenderTexture();

            videoPlayer.isLooping = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoImage.texture = renderTexture;
            videoImage.color = Color.white;

            if (openingVideoClip != null)
            {
                videoPlayer.source = VideoSource.VideoClip;
                videoPlayer.clip = openingVideoClip;
                return true;
            }

            string videoPath = ResolveVideoPath(openingVideoPath);
            if (File.Exists(videoPath))
            {
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = videoPath.Replace('\\', '/');
                Debug.Log($"OpeningVideoPlayer: playing opening video from {videoPlayer.url}");
                return true;
            }

            Debug.LogWarning($"OpeningVideoPlayer: opening video was not found: {videoPath}");
            SetVisible(false);
            return false;
        }

        private bool EnsureOverlay()
        {
            if (!TryResolveSceneCamera(out Camera sceneCamera))
            {
                return false;
            }

            if (canvas != null && videoImage != null)
            {
                ConfigureCanvas(sceneCamera);
                return true;
            }

            GameObject canvasObject = new GameObject(OpeningCanvasName, typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            ConfigureCanvas(sceneCamera);

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DefaultRenderTextureSize.x, DefaultRenderTextureSize.y);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject imageObject = new GameObject(OpeningImageName, typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);
            videoImage = imageObject.AddComponent<RawImage>();
            videoImage.raycastTarget = false;
            videoImage.color = backgroundColor;

            RectTransform imageRect = videoImage.rectTransform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            SetVisible(false);
            return true;
        }

        private void EnsureVideoPlayer()
        {
            if (videoPlayer != null)
            {
                return;
            }

            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
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
                Destroy(renderTexture);
            }

            renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "OpeningVideoRenderTexture"
            };
            renderTexture.Create();
        }

        private void ConfigureCanvas(Camera sceneCamera)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = sceneCamera;
            canvas.planeDistance = ResolveCanvasPlaneDistance(sceneCamera);
            canvas.sortingOrder = sortingOrder;
        }

        private static bool TryResolveSceneCamera(out Camera sceneCamera)
        {
            sceneCamera = Camera.main;
            if (sceneCamera != null)
            {
                return true;
            }

            Debug.LogError("OpeningVideoPlayer: MainCamera not found. Opening video display must follow the scene camera.");
            return false;
        }

        private static float ResolveCanvasPlaneDistance(Camera sceneCamera)
        {
            float minimumDistance = sceneCamera.nearClipPlane + 0.01f;
            float preferredDistance = sceneCamera.nearClipPlane + 1.0f;
            float maximumDistance = sceneCamera.farClipPlane - 0.01f;
            return maximumDistance > minimumDistance
                ? Mathf.Clamp(preferredDistance, minimumDistance, maximumDistance)
                : minimumDistance;
        }

        private string ResolveVideoPath(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(videoPath))
            {
                return Path.GetFullPath(videoPath);
            }

            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, videoPath));
        }

        private void HandleLoopPointReached(VideoPlayer _source)
        {
            playbackCompleted = true;
        }

        private void StopPlayback()
        {
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleLoopPointReached;
                videoPlayer.Stop();
            }

            playbackCompleted = false;
            SetVisible(false);
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
            StopPlayback();
        }

        private void OnDestroy()
        {
            StopPlayback();

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }
    }
}
