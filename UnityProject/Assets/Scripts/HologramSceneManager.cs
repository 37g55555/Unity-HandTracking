using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ShadowPrototype;

public class HologramSceneManager : MonoBehaviour
{
    private const string ReturnSceneName = "Main";

    private bool isClosing;

    private void Start()
    {
        int targetDisplay = Display.displays.Length > 1 ? 1 : 0;
        if (targetDisplay == 1)
        {
            Display.displays[1].Activate();
        }

        ApplyTargetDisplay(gameObject.scene, targetDisplay);
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
}
