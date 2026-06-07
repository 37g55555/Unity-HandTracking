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
        [SerializeField] private VideoClip fogVideoClip;
        [SerializeField] private string fogVideoPath = "../fog.mp4";
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
            if (currentState == GameStateManager.PipelineState.Reconstructing3D)
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

            if (!TryApplyVideoSource())
            {
                fogActive = false;
                SetVisible(false);
                transitionRoutine = null;
                yield break;
            }

            canvasGroup.alpha = 0f;
            SetVisible(true);

            videoPlayer.Stop();
            videoPlayer.Prepare();

            float prepareDeadline = Time.realtimeSinceStartup + 5f;
            while (!videoPlayer.isPrepared && Time.realtimeSinceStartup < prepareDeadline)
            {
                yield return null;
            }

            videoPlayer.Play();
            yield return FadeTo(1f, fadeInDuration);
            transitionRoutine = null;
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
            canvas.targetDisplay = 0;
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
            videoPlayer.isLooping = true;
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

        private bool TryApplyVideoSource()
        {
            if (fogVideoClip != null)
            {
                videoPlayer.source = VideoSource.VideoClip;
                videoPlayer.clip = fogVideoClip;
                return true;
            }

            string videoPath = ResolveFogVideoPath();
            if (File.Exists(videoPath))
            {
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = new Uri(videoPath).AbsoluteUri;
                return true;
            }

            Debug.LogWarning($"SmokeTransitionEffect: fog video was not found: {videoPath}");
            return false;
        }

        private string ResolveFogVideoPath()
        {
            if (string.IsNullOrWhiteSpace(fogVideoPath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(fogVideoPath))
            {
                return Path.GetFullPath(fogVideoPath);
            }

            string unityProjectDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string projectRelativePath = Path.GetFullPath(Path.Combine(unityProjectDirectory, fogVideoPath));
            if (File.Exists(projectRelativePath))
            {
                return projectRelativePath;
            }

            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, fogVideoPath));
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
        }

        private void StopFogPlayback()
        {
            fogActive = false;
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
            }
        }

        private void SetVisible(bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }
        }

    }
}
