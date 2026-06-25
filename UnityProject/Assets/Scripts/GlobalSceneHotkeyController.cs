using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class GlobalSceneHotkeyController : MonoBehaviour
    {
        private static readonly string[] SceneOrder =
        {
            "Opening",
            "Mission1",
            "Mission2",
            "Mission3",
            "Mission4",
            "Mission5",
            "Ending"
        };

        private static GlobalSceneHotkeyController instance;
        private static bool returnToOpeningInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            GlobalSceneHotkeyController existing = FindObjectOfType<GlobalSceneHotkeyController>();
            if (existing != null)
            {
                instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            GameObject controllerObject = new GameObject("GlobalSceneHotkeyController");
            DontDestroyOnLoad(controllerObject);
            instance = controllerObject.AddComponent<GlobalSceneHotkeyController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ReturnToOpeningForNextAudience();
                return;
            }

            if (Input.GetKeyDown(KeyCode.W))
            {
                AdvanceCurrentSceneOrPhase();
            }
        }

        private static void ReturnToOpeningForNextAudience()
        {
            if (returnToOpeningInProgress)
            {
                return;
            }

            returnToOpeningInProgress = true;
            LoadScene(SceneOrder[0]);
            returnToOpeningInProgress = false;
        }

        private void AdvanceCurrentSceneOrPhase()
        {
            if (TryAdvanceOpening() ||
                TryAdvanceMission1() ||
                TryAdvanceMission2() ||
                TryAdvanceMission3() ||
                TryAdvanceMission4() ||
                TryAdvanceMission5() ||
                TryAdvanceEnding())
            {
                return;
            }

            LoadNextSceneByActiveScene();
        }

        private static bool TryAdvanceOpening()
        {
            PipelineManager pipelineManager = FindObjectOfType<PipelineManager>();
            if (pipelineManager == null)
            {
                return false;
            }

            pipelineManager.DebugAdvanceToMission1();
            return true;
        }

        private static bool TryAdvanceMission1()
        {
            Mission1Controller controller = FindObjectOfType<Mission1Controller>();
            if (controller == null)
            {
                return false;
            }

            controller.DebugAdvance();
            return true;
        }

        private static bool TryAdvanceMission2()
        {
            Mission2StarMeshIntroAnimator controller = FindObjectOfType<Mission2StarMeshIntroAnimator>();
            if (controller == null)
            {
                return false;
            }

            controller.DebugAdvance();
            return true;
        }

        private static bool TryAdvanceMission3()
        {
            Mission3HologramDisplayLoader loader = FindObjectOfType<Mission3HologramDisplayLoader>();
            if (loader != null)
            {
                loader.DebugAdvance();
                return true;
            }

            HologramSwipeRotationSystem swipeSystem = FindObjectOfType<HologramSwipeRotationSystem>();
            if (swipeSystem == null)
            {
                return false;
            }

            swipeSystem.DebugAdvanceToNextScene();
            return true;
        }

        private static bool TryAdvanceMission4()
        {
            Mission4Controller controller = FindObjectOfType<Mission4Controller>();
            if (controller == null)
            {
                return false;
            }

            controller.DebugAdvance();
            return true;
        }

        private static bool TryAdvanceMission5()
        {
            Mission5Controller controller = FindObjectOfType<Mission5Controller>();
            if (controller != null)
            {
                controller.DebugAdvance();
                return true;
            }

            Mission5SeesawShadowSystem shadowSystem = FindObjectOfType<Mission5SeesawShadowSystem>();
            if (shadowSystem == null)
            {
                return false;
            }

            shadowSystem.DebugTriggerCompletion();
            return true;
        }

        private static bool TryAdvanceEnding()
        {
            EndingVideoSequenceController endingController = FindObjectOfType<EndingVideoSequenceController>();
            if (endingController == null)
            {
                return false;
            }

            endingController.SkipToEndingModel();
            return true;
        }

        private static void LoadNextSceneByActiveScene()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            for (int i = 0; i < SceneOrder.Length - 1; i++)
            {
                if (string.Equals(SceneOrder[i], currentSceneName, System.StringComparison.Ordinal))
                {
                    LoadScene(SceneOrder[i + 1]);
                    return;
                }
            }
        }

        private static void LoadScene(string sceneName)
        {
            SceneFlowController sceneFlowController = FindObjectOfType<SceneFlowController>();
            if (sceneFlowController != null)
            {
                sceneFlowController.LoadScene(sceneName);
                return;
            }

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
