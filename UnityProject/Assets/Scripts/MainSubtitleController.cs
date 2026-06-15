using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public class MainSubtitleController : MonoBehaviour
    {
        private const string SubtitleCanvasName = "MainSubtitleCanvas";
        private const string SubtitlePanelName = "MainSubtitlePanel";
        private const string SubtitleTextName = "MainSubtitleText";
        private const string DefaultSubtitleFontResourcePath = "Fonts/KoPubWorld Batang Medium";
        private const string DefaultKeywordResultFormat = "\uB9C8\uC9C0\uB9C9 \uADF8\uB9BC\uC790\uB294 {0} \uBAA8\uC591\uC744 \uD558\uACE0 \uC788\uC5C8\uC5B4\uC694.";

        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private int sortingOrder = 3500;
        [SerializeField] private string keywordResultFormat = DefaultKeywordResultFormat;
        [SerializeField] private Font subtitleFont;
        [SerializeField, Min(0f)] private float fadeSeconds = 0.2f;
        [SerializeField, Min(12)] private int fontSize = 36;
        [SerializeField, Min(0f)] private float bottomMargin = 64f;
        [SerializeField, Range(0.2f, 1f)] private float widthRatio = 0.9f;
        [SerializeField, Min(32f)] private float panelHeight = 96f;
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.58f);
        [SerializeField] private Color textColor = Color.white;

        private GameObject subtitleCanvasObject;
        private CanvasGroup canvasGroup;
        private Text subtitleText;
        private Coroutine subtitleRoutine;

        private void Awake()
        {
            if (stateManager == null)
            {
                stateManager = FindObjectOfType<GameStateManager>();
            }

            CreateSubtitleCanvas();
            HideImmediately();
        }

        private void OnEnable()
        {
            if (stateManager == null)
            {
                return;
            }

            stateManager.KeywordChanged += HandleKeywordChanged;
            stateManager.StateChanged += HandleStateChanged;

            if (stateManager.CurrentState != GameStateManager.PipelineState.Mission1 &&
                !string.IsNullOrWhiteSpace(stateManager.Keyword))
            {
                ShowKeywordResult(stateManager.Keyword);
            }
        }

        private void OnDisable()
        {
            if (stateManager != null)
            {
                stateManager.KeywordChanged -= HandleKeywordChanged;
                stateManager.StateChanged -= HandleStateChanged;
            }

            StopSubtitleRoutine();
            HideImmediately();
        }

        private void OnDestroy()
        {
            if (subtitleCanvasObject != null)
            {
                Destroy(subtitleCanvasObject);
                subtitleCanvasObject = null;
            }
        }

        private void HandleKeywordChanged(string keyword)
        {
            ShowKeywordResult(keyword);
        }

        private void HandleStateChanged(GameStateManager.PipelineState nextState)
        {
            if (nextState == GameStateManager.PipelineState.Mission1)
            {
                HideKeywordResult();
            }
        }

        private void ShowKeywordResult(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            StopSubtitleRoutine();

            string format = string.IsNullOrWhiteSpace(keywordResultFormat)
                ? DefaultKeywordResultFormat
                : keywordResultFormat;

            string message;
            try
            {
                message = string.Format(format, keyword.Trim());
            }
            catch (FormatException)
            {
                message = string.Format(DefaultKeywordResultFormat, keyword.Trim());
            }

            if (!CanStartSubtitleCoroutine())
            {
                return;
            }

            subtitleRoutine = StartCoroutine(ShowSubtitleRoutine(message));
        }

        public void ShowMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            StopSubtitleRoutine();
            if (!CanStartSubtitleCoroutine())
            {
                return;
            }

            subtitleRoutine = StartCoroutine(ShowSubtitleRoutine(message.Trim()));
        }

        private IEnumerator ShowSubtitleRoutine(string message)
        {
            SetSubtitleText(message);
            yield return FadeTo(1f);
            subtitleRoutine = null;
        }

        public void HideKeywordResult()
        {
            StopSubtitleRoutine();
            if (!CanStartSubtitleCoroutine())
            {
                HideImmediately();
                return;
            }

            subtitleRoutine = StartCoroutine(HideSubtitleRoutine());
        }

        public void HideMessage()
        {
            HideKeywordResult();
        }

        public IEnumerator HideKeywordResultAndWait()
        {
            StopSubtitleRoutine();
            yield return HideSubtitleRoutine();
        }

        public IEnumerator HideMessageAndWait()
        {
            yield return HideKeywordResultAndWait();
        }

        private IEnumerator HideSubtitleRoutine()
        {
            yield return FadeTo(0f);
            SetSubtitleText(string.Empty);
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
                float eased = Mathf.SmoothStep(0f, 1f, t);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
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

        private bool CanStartSubtitleCoroutine()
        {
            return isActiveAndEnabled && gameObject.activeInHierarchy;
        }

        private void HideImmediately()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            SetSubtitleText(string.Empty);
        }

        private void SetSubtitleText(string message)
        {
            if (subtitleText != null)
            {
                subtitleText.text = message;
            }
        }

        private void CreateSubtitleCanvas()
        {
            if (!TryResolveSceneCamera(out Camera sceneCamera))
            {
                return;
            }

            subtitleCanvasObject = new GameObject(SubtitleCanvasName, typeof(RectTransform));
            subtitleCanvasObject.transform.SetParent(transform, false);

            Canvas canvas = subtitleCanvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = sceneCamera;
            canvas.planeDistance = ResolveCanvasPlaneDistance(sceneCamera);
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = subtitleCanvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGroup = subtitleCanvasObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            CreateSubtitlePanel();
        }

        private void CreateSubtitlePanel()
        {
            GameObject panelObject = new GameObject(SubtitlePanelName, typeof(RectTransform));
            panelObject.transform.SetParent(subtitleCanvasObject.transform, false);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, bottomMargin);
            panelRect.sizeDelta = new Vector2(1920f * widthRatio, panelHeight);

            Image panelBackground = panelObject.AddComponent<Image>();
            panelBackground.color = backgroundColor;
            panelBackground.raycastTarget = false;

            GameObject textObject = new GameObject(SubtitleTextName, typeof(RectTransform));
            textObject.transform.SetParent(panelObject.transform, false);

            subtitleText = textObject.AddComponent<Text>();
            subtitleText.font = CreateSubtitleFont();
            subtitleText.fontSize = fontSize;
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.color = textColor;
            subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subtitleText.verticalOverflow = VerticalWrapMode.Truncate;
            subtitleText.raycastTarget = false;
            subtitleText.supportRichText = false;

            Shadow textShadow = textObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            textShadow.effectDistance = new Vector2(2f, -2f);
            textShadow.useGraphicAlpha = true;

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 8f);
            textRect.offsetMax = new Vector2(-28f, -8f);
        }

        private static bool TryResolveSceneCamera(out Camera sceneCamera)
        {
            sceneCamera = Camera.main;
            if (sceneCamera == null)
            {
                Debug.LogError("MainSubtitleController: MainCamera not found. Subtitle display must follow the scene camera.");
                return false;
            }

            return true;
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

        private Font CreateSubtitleFont()
        {
            if (subtitleFont != null)
            {
                return subtitleFont;
            }

            Font resourceFont = Resources.Load<Font>(DefaultSubtitleFontResourcePath);
            if (resourceFont != null)
            {
                return resourceFont;
            }

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
                Debug.LogWarning($"MainSubtitleController: failed to create subtitle font. {exception.Message}");
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
