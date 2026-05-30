using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ShadowPrototype;

public class HologramSceneManager : MonoBehaviour
{
    private const string ReturnSceneName = "Main";

    [SerializeField] private Vector3 sceneWorldOffset = new Vector3(50f, 0f, 0f);

    private bool isClosing;

    private void Start()
    {
        ApplySceneWorldOffset(gameObject.scene, sceneWorldOffset);

        int targetDisplay = Display.displays.Length > 1 ? 1 : 0;
        if (targetDisplay == 1)
        {
            Display.displays[1].Activate();
        }

        ApplyTargetDisplay(gameObject.scene, targetDisplay);
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
