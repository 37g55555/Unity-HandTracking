using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public class HologramSubtitleController : MonoBehaviour
    {
        private const string SubtitleCanvasName = "HologramSubtitleCanvas";
        private const string SubtitleClearCameraName = "HologramSubtitleClearCamera";
        private const float ClearCameraDepth = -1000f;

        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private string subtitleFileName = "hologram_subtitles.txt";
        [SerializeField, Min(0)] private int targetDisplayIndex = 1;
        [SerializeField] private int sortingOrder = 1200;
        [SerializeField, Min(0.5f)] private float messageDisplaySeconds = 4f;
        [SerializeField, Min(0f)] private float fadeSeconds = 0.35f;
        [SerializeField, Min(12)] private int fontSize = 44;
        [SerializeField, Min(0f)] private float bottomOffsetPixels = 80f;

        private readonly Dictionary<GameStateManager.PipelineState, List<SubtitleCue>> cuesByState =
            new Dictionary<GameStateManager.PipelineState, List<SubtitleCue>>();

        private GameObject subtitleCanvasObject;
        private GameObject clearCameraObject;
        private CanvasGroup canvasGroup;
        private Text subtitleText;
        private Coroutine subtitleRoutine;
        private bool hasActiveState;
        private GameStateManager.PipelineState activeState;

        private void Awake()
        {
            if (stateManager == null)
            {
                stateManager = FindObjectOfType<GameStateManager>();
            }

            LoadSubtitleFile();
            CreateSubtitleCanvas();
        }

        private void OnEnable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged += HandleStateChanged;
                ShowState(stateManager.CurrentState);
            }
        }

        private void OnDisable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged -= HandleStateChanged;
            }

            StopSubtitleRoutine();
            HideImmediately();
            hasActiveState = false;
        }

        private void OnDestroy()
        {
            DestroySubtitleObjects();
        }

        private void Update()
        {
            if (stateManager == null)
            {
                return;
            }

            if (!hasActiveState || stateManager.CurrentState != activeState)
            {
                ShowState(stateManager.CurrentState);
            }
        }

        private void HandleStateChanged(GameStateManager.PipelineState nextState)
        {
            ShowState(nextState);
        }

        private void ShowState(GameStateManager.PipelineState nextState)
        {
            if (hasActiveState && activeState == nextState)
            {
                return;
            }

            hasActiveState = true;
            activeState = nextState;
            StopSubtitleRoutine();
            HideImmediately();

            List<SubtitleCue> cues;
            if (!cuesByState.TryGetValue(nextState, out cues) || cues.Count == 0)
            {
                return;
            }

            subtitleRoutine = StartCoroutine(PlaySubtitleCues(cues));
        }

        private IEnumerator PlaySubtitleCues(List<SubtitleCue> cues)
        {
            int cueIndex = 0;

            while (true)
            {
                subtitleText.text = cues[cueIndex].Message;
                yield return FadeTo(1f);
                yield return new WaitForSecondsRealtime(messageDisplaySeconds);

                if (cues.Count <= 1)
                {
                    break;
                }

                yield return FadeTo(0f);
                cueIndex = (cueIndex + 1) % cues.Count;
            }

            subtitleRoutine = null;
        }

        private IEnumerator FadeTo(float targetAlpha)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            if (fadeSeconds <= 0f)
            {
                canvasGroup.alpha = targetAlpha;
                yield break;
            }

            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeSeconds);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
        }

        private void StopSubtitleRoutine()
        {
            if (subtitleRoutine == null)
            {
                return;
            }

            StopCoroutine(subtitleRoutine);
            subtitleRoutine = null;
        }

        private void HideImmediately()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (subtitleText != null)
            {
                subtitleText.text = string.Empty;
            }
        }

        private void LoadSubtitleFile()
        {
            cuesByState.Clear();

            string subtitlePath = Path.Combine(Application.streamingAssetsPath, subtitleFileName);
            if (!File.Exists(subtitlePath))
            {
                Debug.LogWarning($"HologramSubtitleController: subtitle file not found: {subtitlePath}");
                return;
            }

            string[] lines = File.ReadAllLines(subtitlePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '|' }, 3);
                if (parts.Length < 3)
                {
                    Debug.LogWarning($"HologramSubtitleController: invalid subtitle line {i + 1}: {line}");
                    continue;
                }

                int order;
                if (!int.TryParse(parts[0].Trim(), out order))
                {
                    Debug.LogWarning($"HologramSubtitleController: invalid subtitle order at line {i + 1}: {line}");
                    continue;
                }

                GameStateManager.PipelineState state;
                if (!TryResolveState(parts[1], out state))
                {
                    Debug.LogWarning($"HologramSubtitleController: unknown subtitle state at line {i + 1}: {parts[1]}");
                    continue;
                }

                string message = parts[2].Trim();
                if (message.Length == 0)
                {
                    continue;
                }

                List<SubtitleCue> cues;
                if (!cuesByState.TryGetValue(state, out cues))
                {
                    cues = new List<SubtitleCue>();
                    cuesByState.Add(state, cues);
                }

                cues.Add(new SubtitleCue(order, message));
            }

            foreach (KeyValuePair<GameStateManager.PipelineState, List<SubtitleCue>> pair in cuesByState)
            {
                pair.Value.Sort((left, right) => left.Order.CompareTo(right.Order));
            }
        }

        private static bool TryResolveState(string rawState, out GameStateManager.PipelineState state)
        {
            GameStateManager.PipelineState parsedState;
            if (Enum.TryParse(rawState.Trim(), true, out parsedState))
            {
                state = parsedState;
                return true;
            }

            string key = rawState.Trim().ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty);

            switch (key)
            {
                case "shadowcapturing":
                case "shadowcapture":
                case "capture":
                    state = GameStateManager.PipelineState.ShadowCapturing;
                    return true;

                case "mediapipetracking":
                case "mediapipe":
                case "tracking":
                case "handtracking":
                    state = GameStateManager.PipelineState.MediaPipeTracking;
                    return true;

                case "meshextracting":
                case "meshextraction":
                case "extracting":
                    state = GameStateManager.PipelineState.MeshExtracting;
                    return true;

                case "reconstructing3d":
                case "reconstructing":
                case "reconstruction":
                case "sf3d":
                    state = GameStateManager.PipelineState.Reconstructing3D;
                    return true;

                case "hologramoutput":
                case "hologram":
                case "output":
                    state = GameStateManager.PipelineState.HologramOutput;
                    return true;

                case "error":
                    state = GameStateManager.PipelineState.Error;
                    return true;
            }

            state = GameStateManager.PipelineState.ShadowCapturing;
            return false;
        }

        private void CreateSubtitleCanvas()
        {
            int activeDisplay = ResolveTargetDisplayIndex();
            CreateClearCamera(activeDisplay);

            subtitleCanvasObject = new GameObject(SubtitleCanvasName, typeof(RectTransform));
            subtitleCanvasObject.transform.SetParent(transform, false);

            Canvas canvas = subtitleCanvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (activeDisplay > 0)
            {
                Display.displays[activeDisplay].Activate();
            }

            canvas.targetDisplay = activeDisplay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = subtitleCanvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGroup = subtitleCanvasObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            GameObject textObject = new GameObject("SubtitleText", typeof(RectTransform));
            textObject.transform.SetParent(subtitleCanvasObject.transform, false);

            subtitleText = textObject.AddComponent<Text>();
            subtitleText.font = CreateSubtitleFont();
            subtitleText.fontSize = fontSize;
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.color = Color.white;
            subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subtitleText.verticalOverflow = VerticalWrapMode.Truncate;
            subtitleText.raycastTarget = false;
            subtitleText.supportRichText = false;

            Shadow textShadow = textObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            textShadow.effectDistance = new Vector2(2f, -2f);
            textShadow.useGraphicAlpha = true;

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0f);
            textRect.anchorMax = new Vector2(0.5f, 0f);
            textRect.pivot = new Vector2(0.5f, 0f);
            textRect.anchoredPosition = new Vector2(0f, bottomOffsetPixels);
            textRect.sizeDelta = new Vector2(1500f, 180f);
        }

        private void CreateClearCamera(int activeDisplay)
        {
            clearCameraObject = new GameObject(SubtitleClearCameraName);
            clearCameraObject.transform.SetParent(transform, false);

            Camera clearCamera = clearCameraObject.AddComponent<Camera>();
            clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = Color.black;
            clearCamera.cullingMask = 0;
            clearCamera.depth = ClearCameraDepth;
            clearCamera.targetDisplay = activeDisplay;
            clearCamera.orthographic = true;
            clearCamera.useOcclusionCulling = false;
            clearCamera.allowHDR = false;
            clearCamera.allowMSAA = false;
        }

        private void DestroySubtitleObjects()
        {
            if (subtitleCanvasObject != null)
            {
                Destroy(subtitleCanvasObject);
                subtitleCanvasObject = null;
            }

            if (clearCameraObject != null)
            {
                Destroy(clearCameraObject);
                clearCameraObject = null;
            }
        }

        private int ResolveTargetDisplayIndex()
        {
            if (Display.displays == null || Display.displays.Length == 0)
            {
                return 0;
            }

            return Mathf.Clamp(targetDisplayIndex, 0, Display.displays.Length - 1);
        }

        private Font CreateSubtitleFont()
        {
            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "Arial" },
                    fontSize);

                if (font != null)
                {
                    return font;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"HologramSubtitleController: failed to create subtitle font. {exception.Message}");
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private struct SubtitleCue
        {
            public SubtitleCue(int order, string message)
            {
                Order = order;
                Message = message;
            }

            public int Order;
            public string Message;
        }
    }
}
