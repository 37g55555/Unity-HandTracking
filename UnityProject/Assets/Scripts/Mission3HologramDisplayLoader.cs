using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class Mission3HologramDisplayLoader : MonoBehaviour
    {
        [SerializeField] private string hologramSceneName = "Mission3_H";
        [SerializeField] private bool loadOnStart = true;

        private Coroutine loadRoutine;

        private IEnumerator Start()
        {
            FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission3);

            if (loadOnStart)
            {
                yield return LoadHologramSceneRoutine();
            }
        }

        public void LoadHologramScene()
        {
            if (loadRoutine != null)
            {
                StopCoroutine(loadRoutine);
            }

            loadRoutine = StartCoroutine(LoadHologramSceneRoutine());
        }

        private IEnumerator LoadHologramSceneRoutine()
        {
            if (string.IsNullOrWhiteSpace(hologramSceneName))
            {
                loadRoutine = null;
                yield break;
            }

            int resolvedDisplay = ResolveTargetDisplayIndex();
            ActivateTargetDisplay(resolvedDisplay);

            Scene hologramScene = SceneManager.GetSceneByName(hologramSceneName);
            if (!hologramScene.IsValid() || !hologramScene.isLoaded)
            {
                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(hologramSceneName, LoadSceneMode.Additive);
                if (loadOperation == null)
                {
                    Debug.LogWarning($"Mission3HologramDisplayLoader: scene could not be loaded: {hologramSceneName}");
                    loadRoutine = null;
                    yield break;
                }

                while (!loadOperation.isDone)
                {
                    yield return null;
                }
            }

            ApplyHologramTargetDisplay(resolvedDisplay);
            yield return null;
            ApplyHologramTargetDisplay(resolvedDisplay);
            loadRoutine = null;
        }

        private int ResolveTargetDisplayIndex()
        {
            if (Display.displays == null || Display.displays.Length == 0)
            {
                return 0;
            }

            return Mathf.Clamp(DisplayRoutingSettings.HologramUnityDisplayIndex, 0, Display.displays.Length - 1);
        }

        private static void ActivateTargetDisplay(int displayIndex)
        {
            if (displayIndex > 0 && displayIndex < Display.displays.Length)
            {
                Display.displays[displayIndex].Activate();
            }
        }

        private void ApplyHologramTargetDisplay(int displayIndex)
        {
            Scene hologramScene = SceneManager.GetSceneByName(hologramSceneName);
            if (!hologramScene.IsValid() || !hologramScene.isLoaded)
            {
                return;
            }

            foreach (GameObject rootObject in hologramScene.GetRootGameObjects())
            {
                Camera[] cameras = rootObject.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameras[i].targetDisplay = displayIndex;
                }

                Canvas[] canvases = rootObject.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].targetDisplay = displayIndex;
                }
            }
        }
    }
}
