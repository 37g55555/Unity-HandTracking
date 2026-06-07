using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HologramSceneManager : MonoBehaviour
{
    private const string ReturnSceneName = "Main";
    private const int TargetDisplayIndex = 1;
    private const string HologramCanvasName = "HologramCanvas";
    private const float HologramPanelMaxSizePixels = 702f;
    private const float HologramPanelScreenFraction = 0.46f;
    private const float HologramPanelPaddingPixels = 24f;
    private static readonly Vector3 SceneWorldOffset = new Vector3(50f, 0f, 0f);

    [SerializeField] private string returnShrinkTargetName = "SoftWhiteCirclePlane";
    [SerializeField, Min(0f)] private float returnShrinkDurationSeconds = 2f;

    private bool isClosing;
    private int activeTargetDisplay;
    private Vector2Int lastLayoutSize;

    private void Start()
    {
        ApplySceneWorldOffset(gameObject.scene, SceneWorldOffset);

        activeTargetDisplay = ResolveTargetDisplayIndex();
        if (activeTargetDisplay > 0)
        {
            Display.displays[activeTargetDisplay].Activate();
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
            StartCoroutine(ReturnToMainRoutine());
        }
    }

    private IEnumerator ReturnToMainRoutine()
    {
        Transform shrinkTarget = FindTransformInLoadedScenes(returnShrinkTargetName);
        if (shrinkTarget == null)
        {
            Debug.LogWarning($"HologramSceneManager: return shrink target was not found: {returnShrinkTargetName}");
            SceneManager.LoadScene(ReturnSceneName, LoadSceneMode.Single);
            yield break;
        }

        Vector3 startScale = shrinkTarget.localScale;
        float elapsed = 0f;
        float duration = Mathf.Max(0f, returnShrinkDurationSeconds);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            shrinkTarget.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, eased);
            yield return null;
        }

        shrinkTarget.localScale = Vector3.zero;
        SceneManager.LoadScene(ReturnSceneName, LoadSceneMode.Single);
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

    private int ResolveTargetDisplayIndex()
    {
        int displayCount = Display.displays.Length;
        if (displayCount <= 1)
        {
            return 0;
        }

        return Mathf.Clamp(TargetDisplayIndex, 0, displayCount - 1);
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
                if (sceneCanvas.name == HologramCanvasName)
                {
                    return sceneCanvas;
                }
            }
        }

        return null;
    }

    private static Transform FindTransformInLoadedScenes(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                Transform found = FindTransformRecursive(rootObject.transform, targetName);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static Transform FindTransformRecursive(Transform root, string targetName)
    {
        if (root.name == targetName)
        {
            return root;
        }

        for (int childIndex = 0; childIndex < root.childCount; childIndex++)
        {
            Transform found = FindTransformRecursive(root.GetChild(childIndex), targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private float CalculatePanelSize(Vector2Int displaySize)
    {
        float displayWidth = Mathf.Max(1f, displaySize.x);
        float displayHeight = Mathf.Max(1f, displaySize.y);
        float fractionSize = Mathf.Min(displayWidth, displayHeight) * HologramPanelScreenFraction;
        float horizontalFit = (displayWidth * 0.5f) - (HologramPanelPaddingPixels * 2f);
        float verticalFit = (displayHeight * 0.5f) - (HologramPanelPaddingPixels * 2f);
        float fittedSize = Mathf.Min(HologramPanelMaxSizePixels, fractionSize, horizontalFit, verticalFit);

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
