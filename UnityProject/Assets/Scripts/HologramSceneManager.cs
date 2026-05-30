using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ShadowPrototype;

public class HologramSceneManager : MonoBehaviour
{
    private const string ReturnSceneName = "Main";

    [SerializeField] private Vector3 sceneWorldOffset = new Vector3(50f, 0f, 0f);
    [SerializeField] private string hologramCanvasName = "HologramCanvas";
    [SerializeField, Min(1f)] private float hologramPanelMaxSizePixels = 702f;
    [SerializeField, Range(0.1f, 1f)] private float hologramPanelScreenFraction = 0.46f;
    [SerializeField, Min(0f)] private float hologramPanelPaddingPixels = 24f;
    [SerializeField] private bool disableHologramCanvasRaycasts = true;

    private bool isClosing;
    private int activeTargetDisplay;
    private Vector2Int lastLayoutSize;

    private void Start()
    {
        ApplySceneWorldOffset(gameObject.scene, sceneWorldOffset);

        activeTargetDisplay = Display.displays.Length > 1 ? 1 : 0;
        if (activeTargetDisplay == 1)
        {
            Display.displays[1].Activate();
        }

        ApplyTargetDisplay(gameObject.scene, activeTargetDisplay);
        ApplyHologramCanvasLayoutIfNeeded(force: true);
    }

    private static void ApplySceneWorldOffset(Scene scene, Vector3 offset)
    {
        if (offset == Vector3.zero)
        {
            return;
        }

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            rootObject.transform.position += offset;
        }

        Debug.Log($"HologramSceneManager: moved hologram scene by world offset {offset}.");
    }

    private static void ApplyTargetDisplay(Scene scene, int targetDisplay)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            Camera[] cameras = rootObject.GetComponentsInChildren<Camera>(true);
            foreach (Camera sceneCamera in cameras)
            {
                sceneCamera.targetDisplay = targetDisplay;
            }

            Canvas[] canvases = rootObject.GetComponentsInChildren<Canvas>(true);
            foreach (Canvas sceneCanvas in canvases)
            {
                sceneCanvas.targetDisplay = targetDisplay;
            }
        }
    }

    private void Update()
    {
        ApplyHologramCanvasLayoutIfNeeded(force: false);

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame ||
            keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            if (isClosing)
            {
                return;
            }

            isClosing = true;
            PipelineManager pipelineManager = FindObjectOfType<PipelineManager>();

            if (SceneManager.sceneCount > 1)
            {
                pipelineManager?.StartPipeline();
                SceneManager.UnloadSceneAsync(gameObject.scene);
                return;
            }

            SceneManager.LoadScene(ReturnSceneName);
        }
    }

    private void ApplyHologramCanvasLayoutIfNeeded(bool force)
    {
        Vector2Int displaySize = GetDisplaySize(activeTargetDisplay);
        if (!force && displaySize == lastLayoutSize)
        {
            return;
        }

        lastLayoutSize = displaySize;
        ApplyHologramCanvasLayout(gameObject.scene, displaySize);
    }

    private void ApplyHologramCanvasLayout(Scene scene, Vector2Int displaySize)
    {
        Canvas hologramCanvas = FindHologramCanvas(scene);
        if (hologramCanvas == null)
        {
            return;
        }

        CanvasScaler scaler = hologramCanvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
        }

        if (disableHologramCanvasRaycasts)
        {
            GraphicRaycaster raycaster = hologramCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false;
            }

            foreach (Graphic graphic in hologramCanvas.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }

        float panelSize = CalculatePanelSize(displaySize);
        SetPanelLayout(hologramCanvas.transform, "Front", new Vector2(0.5f, 0.75f), panelSize);
        SetPanelLayout(hologramCanvas.transform, "Left", new Vector2(0.25f, 0.3f), panelSize);
        SetPanelLayout(hologramCanvas.transform, "Right", new Vector2(0.75f, 0.3f), panelSize);
    }

    private Canvas FindHologramCanvas(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            Canvas[] canvases = rootObject.GetComponentsInChildren<Canvas>(true);
            foreach (Canvas sceneCanvas in canvases)
            {
                if (sceneCanvas.name == hologramCanvasName)
                {
                    return sceneCanvas;
                }
            }
        }

        return null;
    }

    private float CalculatePanelSize(Vector2Int displaySize)
    {
        float displayWidth = Mathf.Max(1f, displaySize.x);
        float displayHeight = Mathf.Max(1f, displaySize.y);
        float fractionSize = Mathf.Min(displayWidth, displayHeight) * hologramPanelScreenFraction;
        float horizontalFit = (displayWidth * 0.5f) - (hologramPanelPaddingPixels * 2f);
        float verticalFit = (displayHeight * 0.5f) - (hologramPanelPaddingPixels * 2f);
        float fittedSize = Mathf.Min(hologramPanelMaxSizePixels, fractionSize, horizontalFit, verticalFit);

        return Mathf.Max(1f, fittedSize);
    }

    private static void SetPanelLayout(Transform canvasTransform, string panelName, Vector2 anchor, float panelSize)
    {
        Transform panel = canvasTransform.Find(panelName);
        if (panel == null || !panel.TryGetComponent(out RectTransform rectTransform))
        {
            return;
        }

        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(panelSize, panelSize);
    }

    private static Vector2Int GetDisplaySize(int targetDisplay)
    {
        if (targetDisplay >= 0 && targetDisplay < Display.displays.Length)
        {
            Display display = Display.displays[targetDisplay];
            if (display.renderingWidth > 0 && display.renderingHeight > 0)
            {
                return new Vector2Int(display.renderingWidth, display.renderingHeight);
            }
        }

        return new Vector2Int(Screen.width, Screen.height);
    }
}
