using ShadowPrototype;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HologramSceneManager : MonoBehaviour
{
    private const int TargetDisplayIndex = DisplayRoutingSettings.HologramUnityDisplayIndex;
    private const string HologramCanvasName = "HologramCanvas";
    private static readonly Vector3 SceneWorldOffset = new Vector3(50f, 0f, 0f);

    [SerializeField] private Camera frontCamera;
    [SerializeField] private Camera leftCamera;
    [SerializeField] private Camera rightCamera;
    [SerializeField] private RenderTexture frontRenderTexture;
    [SerializeField] private RenderTexture leftRenderTexture;
    [SerializeField] private RenderTexture rightRenderTexture;

    private int activeTargetDisplay;
    private Vector2Int lastLayoutSize;

    private void Start()
    {
        ResolveCameraReferences();
        AssignCameraRenderTextures();
        ApplySceneWorldOffset(gameObject.scene, SceneWorldOffset);

        DisplayRoutingSettings.ActivateConfiguredUnityDisplays();
        activeTargetDisplay = ResolveTargetDisplayIndex();
        DisplayRoutingSettings.ActivateUnityDisplay(activeTargetDisplay);

        ApplyTargetDisplay(gameObject.scene, activeTargetDisplay);
        ApplyHologramCanvasLayoutIfNeeded(force: true);
    }

    private void ResolveCameraReferences()
    {
        if (frontCamera == null)
        {
            frontCamera = FindCamera("Cam_Front");
        }

        if (leftCamera == null)
        {
            leftCamera = FindCamera("Cam_Left");
        }

        if (rightCamera == null)
        {
            rightCamera = FindCamera("Cam_Right");
        }
    }

    private Camera FindCamera(string cameraName)
    {
        foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
        {
            Camera[] cameras = rootObject.GetComponentsInChildren<Camera>(true);
            foreach (Camera sceneCamera in cameras)
            {
                if (sceneCamera != null && sceneCamera.name == cameraName)
                {
                    return sceneCamera;
                }
            }
        }

        return null;
    }

    private void AssignCameraRenderTextures()
    {
        AssignCameraRenderTexture(frontCamera, frontRenderTexture);
        AssignCameraRenderTexture(leftCamera, leftRenderTexture);
        AssignCameraRenderTexture(rightCamera, rightRenderTexture);
    }

    private static void AssignCameraRenderTexture(Camera targetCamera, RenderTexture renderTexture)
    {
        if (targetCamera != null)
        {
            targetCamera.targetTexture = renderTexture;
        }
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
    }

    private void ApplyHologramCanvasLayoutIfNeeded(bool force)
    {
        Vector2Int displaySize = HologramPanelLayout.GetDisplaySize(activeTargetDisplay);
        if (!force && displaySize == lastLayoutSize)
        {
            return;
        }

        lastLayoutSize = displaySize;
        ApplyHologramCanvasLayout(gameObject.scene, displaySize);
    }

    private int ResolveTargetDisplayIndex()
    {
        return DisplayRoutingSettings.ResolveUnityDisplayIndex(TargetDisplayIndex);
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

        GraphicRaycaster raycaster = hologramCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
        {
            raycaster.enabled = false;
        }

        foreach (Graphic graphic in hologramCanvas.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        float panelSize = HologramPanelLayout.CalculatePanelSize(displaySize);
        SetPanelLayout(hologramCanvas.transform, "Front", HologramPanelLayout.FrontAnchor, HologramPanelLayout.FrontOffset, panelSize);
        SetPanelLayout(hologramCanvas.transform, "Left", HologramPanelLayout.LeftAnchor, HologramPanelLayout.LeftOffset, panelSize);
        SetPanelLayout(hologramCanvas.transform, "Right", HologramPanelLayout.RightAnchor, HologramPanelLayout.RightOffset, panelSize);
    }

    private Canvas FindHologramCanvas(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            Canvas[] canvases = rootObject.GetComponentsInChildren<Canvas>(true);
            foreach (Canvas sceneCanvas in canvases)
            {
                if (sceneCanvas.name == HologramCanvasName)
                {
                    return sceneCanvas;
                }
            }
        }

        return null;
    }

    private static void SetPanelLayout(Transform canvasTransform, string panelName, Vector2 anchor, Vector2 offset, float panelSize)
    {
        Transform panel = canvasTransform.Find(panelName);
        if (panel == null || !panel.TryGetComponent(out RectTransform rectTransform))
        {
            return;
        }

        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.anchoredPosition = offset;
        rectTransform.sizeDelta = new Vector2(panelSize, panelSize);
    }

}
