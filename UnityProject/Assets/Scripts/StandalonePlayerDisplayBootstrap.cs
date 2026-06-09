using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class StandalonePlayerDisplayBootstrap : MonoBehaviour
    {
        private const string MainSceneName = "Main";
        private const string HologramSubtitleCanvasName = "HologramSubtitleCanvas";
        private const string HologramSubtitleClearCameraName = "HologramSubtitleClearCamera";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateBootstrap()
        {
            var bootstrapObject = new GameObject(nameof(StandalonePlayerDisplayBootstrap));
            DontDestroyOnLoad(bootstrapObject);
            bootstrapObject.AddComponent<StandalonePlayerDisplayBootstrap>();
        }

        private void Awake()
        {
            ActivateAvailableDisplays();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            ApplyProjectorDisplay(SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode _mode)
        {
            ApplyProjectorDisplay(scene);
        }

        private static void ActivateAvailableDisplays()
        {
            for (int index = 1; index < Display.displays.Length; index++)
            {
                Display.displays[index].Activate();
            }
        }

        private static void ApplyProjectorDisplay(Scene scene)
        {
            if (!scene.isLoaded || scene.name != MainSceneName)
            {
                return;
            }

            int targetDisplay = ResolveProjectorDisplayIndex();
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                Camera[] cameras = rootObject.GetComponentsInChildren<Camera>(true);
                foreach (Camera sceneCamera in cameras)
                {
                    if (sceneCamera.name == HologramSubtitleClearCameraName)
                    {
                        continue;
                    }

                    sceneCamera.targetDisplay = targetDisplay;
                }

                Canvas[] canvases = rootObject.GetComponentsInChildren<Canvas>(true);
                foreach (Canvas sceneCanvas in canvases)
                {
                    if (sceneCanvas.name == HologramSubtitleCanvasName)
                    {
                        continue;
                    }

                    sceneCanvas.targetDisplay = targetDisplay;
                }
            }
        }

        private static int ResolveProjectorDisplayIndex()
        {
            if (Display.displays == null || Display.displays.Length == 0)
            {
                return 0;
            }

            return Mathf.Clamp(DisplayRoutingSettings.ProjectorUnityDisplayIndex, 0, Display.displays.Length - 1);
        }
    }
}
