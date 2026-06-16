using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ShadowPrototype
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EndingHologramVideoPlayer : MonoBehaviour
    {
        private static readonly Vector2Int DefaultRenderTextureSize = new Vector2Int(1080, 1080);

        [SerializeField] private string videoRelativePath = "Videos/7 Ending.mp4";
        [SerializeField] private bool playOnStart;
        [SerializeField] private bool loop;
        [SerializeField] private bool hideWhenComplete;
        [SerializeField] private bool previewInEditMode = true;
        [SerializeField] private Color editorPreviewColor = new Color(1.0f, 1.0f, 1.0f, 0.45f);
        [SerializeField, Range(0.0f, 1.0f)] private float audioVolume = 1.0f;
        [SerializeField] private int targetDisplayIndex = DisplayRoutingSettings.HologramUnityDisplayIndex;
        [SerializeField] private int sortingOrder = 5000;
        [SerializeField] private bool autoApplyPanelLayout;
        [SerializeField] private bool showFrontPanel = true;
        [SerializeField] private bool showLeftPanel = true;
        [SerializeField] private bool showRightPanel = true;
        [SerializeField] private Vector2Int renderTextureSize = DefaultRenderTextureSize;
        [SerializeField, Min(0.1f)] private float prepareTimeoutSeconds = 15.0f;
        [SerializeField, Min(0.1f)] private float playbackStallTimeoutSeconds = 15.0f;

        private Canvas canvas;
        private RawImage frontPanel;
        private RawImage leftPanel;
        private RawImage rightPanel;
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;
        private Vector2Int lastLayoutSize;
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
                return;
            }

            ApplyLayoutIfNeeded(false);
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
                yield break;
            }

            playbackCompleted = false;
            videoPlayer.loopPointReached -= HandleLoopPointReached;
            videoPlayer.loopPointReached += HandleLoopPointReached;
            ClearRenderTexture();
            SetVisible(true);

            videoPlayer.Prepare();
            float prepareDeadline = Time.realtimeSinceStartup + prepareTimeoutSeconds;
            while (!videoPlayer.isPrepared && Time.realtimeSinceStartup < prepareDeadline)
            {
                yield return null;
            }

            if (!videoPlayer.isPrepared)
            {
                Debug.LogWarning($"EndingHologramVideoPlayer: prepare timed out: {videoPlayer.url}");
                StopPlayback(true);
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
                    Debug.LogWarning($"EndingHologramVideoPlayer: playback stalled: {videoPlayer.url}");
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
                Debug.LogWarning($"EndingHologramVideoPlayer: video file was not found: {videoUrl}");
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

            AssignPanelTexture(frontPanel);
            AssignPanelTexture(leftPanel);
            AssignPanelTexture(rightPanel);
            ApplyPanelVisibility();
            return true;
        }

        private void RefreshEditorPreview()
        {
            EnsureOverlay();
            EnsureVideoPlayer();
            AssignPanelEditorPreview(frontPanel);
            AssignPanelEditorPreview(leftPanel);
            AssignPanelEditorPreview(rightPanel);
            ApplyPanelVisibility();
            ApplyTargetDisplay();

            if (autoApplyPanelLayout)
            {
                ApplyLayoutIfNeeded(false);
            }

            SetVisible(previewInEditMode);
        }

        private void EnsureOverlay()
        {
            if (canvas != null && frontPanel != null && leftPanel != null && rightPanel != null)
            {
                ApplyTargetDisplay();
                ApplyLayoutIfNeeded(false);
                return;
            }

            Transform existingCanvasTransform = transform.Find("EndingHologramVideoCanvas");
            bool createdOverlay = existingCanvasTransform == null;
            GameObject canvasObject = existingCanvasTransform != null
                ? existingCanvasTransform.gameObject
                : new GameObject("EndingHologramVideoCanvas", typeof(RectTransform));
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

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1.0f;

            frontPanel = FindOrCreatePanel(canvasObject.transform, "EndingVideo_Front", 180.0f);
            leftPanel = FindOrCreatePanel(canvasObject.transform, "EndingVideo_Left", -90.0f);
            rightPanel = FindOrCreatePanel(canvasObject.transform, "EndingVideo_Right", 90.0f);

            ApplyTargetDisplay();
            if (createdOverlay || autoApplyPanelLayout)
            {
                ApplyLayoutIfNeeded(true);
            }
        }

        private static RawImage FindOrCreatePanel(Transform parent, string panelName, float zRotation)
        {
            Transform existingPanel = parent.Find(panelName);
            if (existingPanel != null && existingPanel.TryGetComponent(out RawImage existingImage))
            {
                RectTransform existingRect = existingImage.rectTransform;
                existingRect.localRotation = Quaternion.Euler(0.0f, 0.0f, zRotation);
                existingRect.pivot = new Vector2(0.5f, 0.5f);
                existingImage.raycastTarget = false;
                return existingImage;
            }

            return CreatePanel(parent, panelName, zRotation);
        }

        private static RawImage CreatePanel(Transform parent, string panelName, float zRotation)
        {
            GameObject panelObject = new GameObject(panelName, typeof(RectTransform));
            panelObject.transform.SetParent(parent, false);

            RawImage panel = panelObject.AddComponent<RawImage>();
            panel.raycastTarget = false;
            panel.color = Color.white;

            RectTransform rectTransform = panel.rectTransform;
            rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, zRotation);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return panel;
        }

        private void ApplyTargetDisplay()
        {
            int displayIndex = ResolveTargetDisplayIndex();
            DisplayRoutingSettings.ActivateUnityDisplay(displayIndex);

            if (canvas != null)
            {
                canvas.targetDisplay = displayIndex;
            }
        }

        private int ResolveTargetDisplayIndex()
        {
            return DisplayRoutingSettings.ResolveUnityDisplayIndex(targetDisplayIndex);
        }

        private void ApplyLayoutIfNeeded(bool force)
        {
            if (canvas == null)
            {
                return;
            }

            if (!force && !autoApplyPanelLayout)
            {
                return;
            }

            int displayIndex = ResolveTargetDisplayIndex();
            Vector2Int displaySize = HologramPanelLayout.GetDisplaySize(displayIndex);
            if (!force && displaySize == lastLayoutSize)
            {
                return;
            }

            lastLayoutSize = displaySize;
            float panelSize = HologramPanelLayout.CalculatePanelSize(displaySize);
            ApplyPanelLayout(frontPanel, HologramPanelLayout.FrontAnchor, HologramPanelLayout.FrontOffset, panelSize);
            ApplyPanelLayout(leftPanel, HologramPanelLayout.LeftAnchor, HologramPanelLayout.LeftOffset, panelSize);
            ApplyPanelLayout(rightPanel, HologramPanelLayout.RightAnchor, HologramPanelLayout.RightOffset, panelSize);
        }

        private static void ApplyPanelLayout(RawImage panel, Vector2 anchor, Vector2 offset, float panelSize)
        {
            if (panel == null)
            {
                return;
            }

            RectTransform rectTransform = panel.rectTransform;
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.anchoredPosition = offset;
            rectTransform.sizeDelta = new Vector2(panelSize, panelSize);
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
                name = "EndingHologramVideoRenderTexture"
            };
            renderTexture.Create();
        }

        private void AssignPanelTexture(RawImage panel)
        {
            if (panel != null)
            {
                panel.texture = renderTexture;
                panel.color = Color.white;
            }
        }

        private void ApplyPanelVisibility()
        {
            SetPanelVisible(frontPanel, showFrontPanel);
            SetPanelVisible(leftPanel, showLeftPanel);
            SetPanelVisible(rightPanel, showRightPanel);
        }

        private static void SetPanelVisible(RawImage panel, bool visible)
        {
            if (panel != null)
            {
                panel.enabled = visible;
            }
        }

        private void AssignPanelEditorPreview(RawImage panel)
        {
            if (panel == null)
            {
                return;
            }

            panel.texture = null;
            panel.color = editorPreviewColor;
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

        private void ClearRenderTexture()
        {
            if (renderTexture == null)
            {
                return;
            }

            if (!renderTexture.IsCreated())
            {
                renderTexture.Create();
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previousActive;
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
