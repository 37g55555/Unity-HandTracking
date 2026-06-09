using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ShadowPrototype
{
    public class SmokeTransitionEffect : MonoBehaviour
    {
        private const int SortingOrder = 500;
        private static readonly Vector2Int RenderTextureSize = new Vector2Int(1920, 1080);

        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private VideoClip firstFogVideoClip;
        [SerializeField] private string firstFogVideoPath = "fog_first.mp4";
        [SerializeField] private VideoClip fogVideoClip;
        [SerializeField] private string fogVideoPath = "fog.mp4";
        [SerializeField] private bool chromaKeyEnabled = true;
        [SerializeField] private Shader chromaKeyShader;
        [SerializeField] private Color chromaKeyColor = Color.black;
        [SerializeField, Range(0f, 1f)] private float chromaKeyTolerance = 0.08f;
        [SerializeField, Range(0.001f, 1f)] private float chromaKeySoftness = 0.12f;
        [SerializeField, Min(0f)] private float fadeInDuration = 0.25f;
        [SerializeField, Min(0f)] private float exitDuration = 2f;

        public event Action ExitCompleted;

        private static readonly int KeyColorId = Shader.PropertyToID("_KeyColor");
        private static readonly int KeyToleranceId = Shader.PropertyToID("_KeyTolerance");
        private static readonly int KeySoftnessId = Shader.PropertyToID("_KeySoftness");

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private RawImage fogImage;
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;
        private Material runtimeChromaKeyMaterial;
        private Coroutine transitionRoutine;
        private bool fogActive;
        private bool introVideoCompleted;
        private bool waitingForIntroVideo;

        private void Awake()
        {
            CreateOverlay();
            CreateVideoPlayer();
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged -= HandleStateChanged;
            }

            StopTransitionRoutine();
            StopFogPlayback();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            StopTransitionRoutine();
            StopFogPlayback();

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }

            if (runtimeChromaKeyMaterial != null)
            {
                Destroy(runtimeChromaKeyMaterial);
                runtimeChromaKeyMaterial = null;
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyChromaKeyMaterial();
            }
        }

        private void HandleStateChanged(GameStateManager.PipelineState currentState)
        {
            if (currentState == GameStateManager.PipelineState.MeshExtracting)
            {
                BeginFogLoop();
            }
            else if (currentState == GameStateManager.PipelineState.HologramOutput)
            {
                BeginExit();
            }
        }

        private void BeginFogLoop()
        {
            StopTransitionRoutine();
            transitionRoutine = StartCoroutine(PlayFogLoopRoutine());
        }

        private IEnumerator PlayFogLoopRoutine()
        {
            fogActive = true;
            EnsureRenderTexture();
            ConfigureVideoPlayer();
            ApplyChromaKeyMaterial();

            canvasGroup.alpha = 0f;
            SetVisible(true);

            bool hasIntroVideo = TryApplyVideoSource(firstFogVideoClip, firstFogVideoPath, loop: false, warnIfMissing: false);
            if (hasIntroVideo)
            {
                yield return PlayCurrentVideo(waitForCompletion: true, fadeIn: true);
            }

            if (!fogActive)
            {
                transitionRoutine = null;
                yield break;
            }

            if (!TryApplyVideoSource(fogVideoClip, fogVideoPath, loop: true, warnIfMissing: true))
            {
                fogActive = false;
                SetVisible(false);
                transitionRoutine = null;
                yield break;
            }

            yield return PlayCurrentVideo(waitForCompletion: false, fadeIn: !hasIntroVideo);
            transitionRoutine = null;
        }

        private IEnumerator PlayCurrentVideo(bool waitForCompletion, bool fadeIn)
        {
            videoPlayer.Stop();
            videoPlayer.Prepare();

            float prepareDeadline = Time.realtimeSinceStartup + 5f;
            while (!videoPlayer.isPrepared && Time.realtimeSinceStartup < prepareDeadline)
            {
                yield return null;
            }

            if (waitForCompletion)
            {
                AddIntroVideoFinishedHandler();
            }

            videoPlayer.Play();

            if (fadeIn)
            {
                yield return FadeTo(1f, fadeInDuration);
            }

            if (!waitForCompletion)
            {
                yield break;
            }

            while (fogActive && !introVideoCompleted && videoPlayer != null)
            {
                if (!videoPlayer.isPlaying && videoPlayer.frame > 0)
                {
                    break;
                }

                yield return null;
            }

            RemoveIntroVideoFinishedHandler();
        }

        private void BeginExit()
        {
            StopTransitionRoutine();

            if (!fogActive && (canvas == null || !canvas.enabled))
            {
                ExitCompleted?.Invoke();
                return;
            }

            transitionRoutine = StartCoroutine(ExitRoutine());
        }

        private IEnumerator ExitRoutine()
        {
            fogActive = false;
            yield return FadeTo(0f, exitDuration);
            StopFogPlayback();
            SetVisible(false);
            ExitCompleted?.Invoke();
            transitionRoutine = null;
        }

        private void CreateOverlay()
        {
            GameObject canvasObject = new GameObject("FogTransitionCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.targetDisplay = ResolveProjectorDisplayIndex();
            canvasObject.AddComponent<CanvasScaler>();

            canvasGroup = canvasObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            GameObject imageObject = new GameObject("FogVideo", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);

            fogImage = imageObject.AddComponent<RawImage>();
            fogImage.raycastTarget = false;
            fogImage.color = Color.white;
            ApplyChromaKeyMaterial();

            RectTransform imageRect = fogImage.rectTransform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
        }

        private static int ResolveProjectorDisplayIndex()
        {
            if (Display.displays == null || Display.displays.Length == 0)
            {
                return 0;
            }

            return Mathf.Clamp(DisplayRoutingSettings.ProjectorUnityDisplayIndex, 0, Display.displays.Length - 1);
        }

        private void CreateVideoPlayer()
        {
            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.isLooping = true;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        }

        private void ConfigureVideoPlayer()
        {
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            fogImage.texture = renderTexture;
        }

        private void ApplyChromaKeyMaterial()
        {
            if (fogImage == null)
            {
                return;
            }

            if (!chromaKeyEnabled)
            {
                fogImage.material = null;
                return;
            }

            Shader shader = chromaKeyShader != null
                ? chromaKeyShader
                : Shader.Find("UI/ChromaKeyVideo");

            if (shader == null)
            {
                Debug.LogWarning("SmokeTransitionEffect: chroma key shader was not found.");
                fogImage.material = null;
                return;
            }

            if (runtimeChromaKeyMaterial == null || runtimeChromaKeyMaterial.shader != shader)
            {
                if (runtimeChromaKeyMaterial != null)
                {
                    Destroy(runtimeChromaKeyMaterial);
                }

                runtimeChromaKeyMaterial = new Material(shader)
                {
                    name = "FogChromaKeyRuntimeMaterial",
                    hideFlags = HideFlags.DontSave
                };
            }

            runtimeChromaKeyMaterial.SetColor(KeyColorId, chromaKeyColor);
            runtimeChromaKeyMaterial.SetFloat(KeyToleranceId, chromaKeyTolerance);
            runtimeChromaKeyMaterial.SetFloat(KeySoftnessId, chromaKeySoftness);
            fogImage.material = runtimeChromaKeyMaterial;
        }

        private bool TryApplyVideoSource(VideoClip clip, string path, bool loop, bool warnIfMissing)
        {
            videoPlayer.isLooping = loop;
            if (clip != null)
            {
                videoPlayer.source = VideoSource.VideoClip;
                videoPlayer.clip = clip;
                return true;
            }

            string videoPath = ResolveVideoPath(path);
            if (File.Exists(videoPath))
            {
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = new Uri(videoPath).AbsoluteUri;
                return true;
            }

            if (warnIfMissing)
            {
                Debug.LogWarning($"SmokeTransitionEffect: fog video was not found: {videoPath}");
            }

            return false;
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

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(16, RenderTextureSize.x);
            int height = Mathf.Max(16, RenderTextureSize.y);

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
                name = "FogTransitionRenderTexture"
            };
            renderTexture.Create();
        }

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            float startAlpha = canvasGroup.alpha;
            if (duration <= 0f)
            {
                canvasGroup.alpha = targetAlpha;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
        }

        private void StopTransitionRoutine()
        {
            if (transitionRoutine == null)
            {
                return;
            }

            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
            RemoveIntroVideoFinishedHandler();
        }

        private void StopFogPlayback()
        {
            fogActive = false;
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
            }

            RemoveIntroVideoFinishedHandler();
        }

        private void SetVisible(bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }
        }

        private void AddIntroVideoFinishedHandler()
        {
            if (waitingForIntroVideo)
            {
                return;
            }

            introVideoCompleted = false;
            waitingForIntroVideo = true;
            videoPlayer.loopPointReached += HandleIntroVideoFinished;
        }

        private void RemoveIntroVideoFinishedHandler()
        {
            if (!waitingForIntroVideo)
            {
                return;
            }

            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleIntroVideoFinished;
            }

            waitingForIntroVideo = false;
        }

        private void HandleIntroVideoFinished(VideoPlayer _source)
        {
            introVideoCompleted = true;
        }

    }
}
