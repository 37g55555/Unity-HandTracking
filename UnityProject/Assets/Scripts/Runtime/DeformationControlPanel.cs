using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ShadowPrototype
{
    public class DeformationControlPanel : MonoBehaviour
    {
        private const string CanvasName = "DeformationControlCanvas";
        private const int LandmarksPerHand = 21;
        private const int ThumbTipIndex = 4;
        private const int MiddleTipIndex = 12;

        [SerializeField] private MediaPipeMeshDeformationInput deformationInput;
        [SerializeField] private HandLandmarkUdpReceiver handReceiver;
        [SerializeField] private float minAffectedRadius = 0.12f;
        [SerializeField] private float maxAffectedRadius = 0.65f;
        [SerializeField] private float startingAffectedRadius = 0.3f;
        [SerializeField] private Vector2 panelSize = new Vector2(58.0f, 300.0f);
        [SerializeField] private Vector2 panelOffset = new Vector2(-34.0f, 0.0f);

        [Header("MediaPipe Slider Control")]
        [SerializeField] private bool enableHandControl = true;
        [SerializeField] private int controllingHandIndex;
        [SerializeField] private float trackedFrameWidth = 1280.0f;
        [SerializeField] private float trackedFrameHeight = 720.0f;
        [SerializeField] private float pinchEnterThresholdPixels = 65.0f;
        [SerializeField] private float pinchExitThresholdPixels = 95.0f;
        [SerializeField] private float sliderGrabPaddingPixels = 90.0f;
        [SerializeField] private float handSliderSmoothingSpeed = 14.0f;

        private Slider slider;
        private RectTransform panelRect;
        private Image panelImage;
        private Image handleImage;
        private bool isHandControllingSlider;

        public void Configure(MediaPipeMeshDeformationInput input, HandLandmarkUdpReceiver receiver = null)
        {
            deformationInput = input;
            handReceiver = receiver;
            ApplyComfortDefaults();
            ApplyStartingAmount();
            SyncSliderToInput();
        }

        private void Start()
        {
            EnsurePanel();
            ApplyComfortDefaults();
            ApplyStartingAmount();
            SyncSliderToInput();
        }

        private void OnDestroy()
        {
            if (slider != null)
            {
                slider.onValueChanged.RemoveListener(HandleSliderChanged);
            }
        }

        private void Update()
        {
            EnsurePanel();
            UpdateHandControl();
        }

        private void EnsurePanel()
        {
            if (slider != null)
            {
                return;
            }

            EnsureEventSystem();

            GameObject canvasObject = new GameObject(CanvasName);
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            panelRect = CreateRectTransform("Deformation Slider", canvasObject.transform);
            panelRect.anchorMin = new Vector2(1.0f, 0.5f);
            panelRect.anchorMax = new Vector2(1.0f, 0.5f);
            panelRect.pivot = new Vector2(1.0f, 0.5f);
            panelRect.sizeDelta = panelSize;
            panelRect.anchoredPosition = panelOffset;

            panelImage = panelRect.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.04f, 0.04f, 0.42f);

            slider = panelRect.gameObject.AddComponent<Slider>();
            slider.minValue = minAffectedRadius;
            slider.maxValue = maxAffectedRadius;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.BottomToTop;

            RectTransform track = CreateRectTransform("Track", panelRect);
            track.anchorMin = new Vector2(0.5f, 0.08f);
            track.anchorMax = new Vector2(0.5f, 0.92f);
            track.pivot = new Vector2(0.5f, 0.5f);
            track.sizeDelta = new Vector2(10.0f, 0.0f);
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(1.0f, 1.0f, 1.0f, 0.22f);

            RectTransform fillArea = CreateRectTransform("Fill Area", panelRect);
            fillArea.anchorMin = track.anchorMin;
            fillArea.anchorMax = track.anchorMax;
            fillArea.pivot = track.pivot;
            fillArea.sizeDelta = new Vector2(10.0f, 0.0f);

            RectTransform fill = CreateRectTransform("Fill", fillArea);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.0f, 0.85f, 0.72f, 0.9f);

            RectTransform handleArea = CreateRectTransform("Handle Slide Area", panelRect);
            handleArea.anchorMin = new Vector2(0.5f, 0.08f);
            handleArea.anchorMax = new Vector2(0.5f, 0.92f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.sizeDelta = new Vector2(42.0f, 0.0f);

            RectTransform handle = CreateRectTransform("Handle", handleArea);
            handle.sizeDelta = new Vector2(34.0f, 34.0f);
            handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(1.0f, 0.93f, 0.25f, 0.96f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.value = deformationInput != null ? deformationInput.AffectedRadiusLocal : startingAffectedRadius;
            slider.onValueChanged.AddListener(HandleSliderChanged);
        }

        private void ApplyComfortDefaults()
        {
            minAffectedRadius = 0.12f;
            maxAffectedRadius = 0.65f;
            startingAffectedRadius = 0.3f;

            if (slider != null)
            {
                slider.minValue = minAffectedRadius;
                slider.maxValue = maxAffectedRadius;
            }
        }

        private void ApplyStartingAmount()
        {
            if (deformationInput != null)
            {
                deformationInput.SetAffectedRadiusLocal(startingAffectedRadius);
            }
        }

        private void UpdateHandControl()
        {
            if (!enableHandControl || slider == null)
            {
                SetHandControlVisual(false);
                return;
            }

            ResolveDependencies();
            if (handReceiver == null || !handReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                isHandControllingSlider = false;
                SetHandControlVisual(false);
                return;
            }

            if (!TryGetHandPoint(landmarks, controllingHandIndex, ThumbTipIndex, out Vector2 thumbTracked) ||
                !TryGetHandPoint(landmarks, controllingHandIndex, MiddleTipIndex, out Vector2 middleTracked))
            {
                isHandControllingSlider = false;
                SetHandControlVisual(false);
                return;
            }

            float pinchDistance = Vector2.Distance(thumbTracked, middleTracked);
            bool isPinching = isHandControllingSlider
                ? pinchDistance <= pinchExitThresholdPixels
                : pinchDistance <= pinchEnterThresholdPixels;

            Vector2 midpointScreen = TrackedPointToScreenPoint((thumbTracked + middleTracked) * 0.5f);
            if (!isPinching)
            {
                isHandControllingSlider = false;
                SetHandControlVisual(IsPointNearPanel(midpointScreen));
                return;
            }

            if (!isHandControllingSlider && !IsPointNearPanel(midpointScreen))
            {
                SetHandControlVisual(false);
                return;
            }

            isHandControllingSlider = true;
            SetSliderValueFromScreenY(midpointScreen.y);
            SetHandControlVisual(true);
        }

        private void ResolveDependencies()
        {
            if (deformationInput == null)
            {
                deformationInput = Object.FindAnyObjectByType<MediaPipeMeshDeformationInput>();
            }

            if (handReceiver == null)
            {
                handReceiver = Object.FindAnyObjectByType<HandLandmarkUdpReceiver>();
            }
        }

        private bool TryGetHandPoint(Vector3[] landmarks, int handIndex, int landmarkIndex, out Vector2 trackedPoint)
        {
            trackedPoint = Vector2.zero;

            int absoluteIndex = (handIndex * LandmarksPerHand) + landmarkIndex;
            if (landmarks == null || absoluteIndex < 0 || absoluteIndex >= landmarks.Length)
            {
                return false;
            }

            Vector3 landmark = landmarks[absoluteIndex];
            trackedPoint = new Vector2(landmark.x, landmark.y);
            return true;
        }

        private Vector2 TrackedPointToScreenPoint(Vector2 trackedPoint)
        {
            float normalizedX = trackedFrameWidth > 0.0f ? Mathf.Clamp01(trackedPoint.x / trackedFrameWidth) : 0.0f;
            float normalizedY = trackedFrameHeight > 0.0f ? Mathf.Clamp01(trackedPoint.y / trackedFrameHeight) : 0.0f;
            return new Vector2(normalizedX * Screen.width, normalizedY * Screen.height);
        }

        private bool IsPointNearPanel(Vector2 screenPoint)
        {
            if (panelRect == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            panelRect.GetWorldCorners(corners);
            float minX = Mathf.Min(corners[0].x, corners[2].x) - sliderGrabPaddingPixels;
            float maxX = Mathf.Max(corners[0].x, corners[2].x) + sliderGrabPaddingPixels;
            float minY = Mathf.Min(corners[0].y, corners[2].y) - sliderGrabPaddingPixels;
            float maxY = Mathf.Max(corners[0].y, corners[2].y) + sliderGrabPaddingPixels;

            return screenPoint.x >= minX &&
                   screenPoint.x <= maxX &&
                   screenPoint.y >= minY &&
                   screenPoint.y <= maxY;
        }

        private void SetSliderValueFromScreenY(float screenY)
        {
            Vector3[] corners = new Vector3[4];
            panelRect.GetWorldCorners(corners);
            float minY = Mathf.Min(corners[0].y, corners[2].y);
            float maxY = Mathf.Max(corners[0].y, corners[2].y);
            float normalized = Mathf.InverseLerp(minY, maxY, screenY);
            float targetValue = Mathf.Lerp(slider.minValue, slider.maxValue, normalized);
            float blend = 1.0f - Mathf.Exp(-handSliderSmoothingSpeed * Time.deltaTime);
            slider.value = Mathf.Lerp(slider.value, targetValue, blend);
        }

        private void SetHandControlVisual(bool active)
        {
            if (panelImage != null)
            {
                panelImage.color = active
                    ? new Color(0.0f, 0.42f, 0.36f, 0.58f)
                    : new Color(0.04f, 0.04f, 0.04f, 0.42f);
            }

            if (handleImage != null)
            {
                handleImage.color = active
                    ? new Color(0.16f, 1.0f, 0.76f, 0.98f)
                    : new Color(1.0f, 0.93f, 0.25f, 0.96f);
            }
        }

        private static RectTransform CreateRectTransform(string objectName, Transform parent)
        {
            GameObject gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<RectTransform>();
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem != null)
            {
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                {
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }

                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private void SyncSliderToInput()
        {
            if (slider == null || deformationInput == null)
            {
                return;
            }

            slider.SetValueWithoutNotify(deformationInput.AffectedRadiusLocal);
        }

        private void HandleSliderChanged(float value)
        {
            if (deformationInput != null)
            {
                deformationInput.SetAffectedRadiusLocal(value);
            }
        }
    }
}
