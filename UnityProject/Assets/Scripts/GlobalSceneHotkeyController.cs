using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace ShadowPrototype
{
    public sealed class GlobalSceneHotkeyController : MonoBehaviour
    {
        private const string ControllerName = "GlobalSceneHotkeyController";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstance()
        {
            if (FindObjectOfType<GlobalSceneHotkeyController>() != null)
            {
                return;
            }

            GameObject controllerObject = new GameObject(ControllerName);
            DontDestroyOnLoad(controllerObject);
            controllerObject.AddComponent<GlobalSceneHotkeyController>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                ReloadCurrentScene();
                return;
            }

            if (Input.GetKeyDown(KeyCode.W))
            {
                SkipToMissionSegment();
                return;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                RestartCurrentMission();
            }
        }

        private static void ReloadCurrentScene()
        {
            StopPlaybackInLoadedScenes();
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrWhiteSpace(activeScene.name))
            {
                return;
            }

            SceneManager.LoadScene(activeScene.name, LoadSceneMode.Single);
        }

        private static void RestartCurrentMission()
        {
            StopPlaybackInLoadedScenes();
            string sceneName = ResolveMissionRestartSceneName();
            if (!string.IsNullOrWhiteSpace(sceneName))
            {
                SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            }
        }

        private static string ResolveMissionRestartSceneName()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            string activeSceneName = activeScene.IsValid() ? activeScene.name : string.Empty;
            if (string.Equals(activeSceneName, "Mission3_H", StringComparison.Ordinal))
            {
                return "Mission3";
            }

            return activeSceneName;
        }

        private static void SkipToMissionSegment()
        {
            StopPlaybackInLoadedScenes();
            StopNarrationInLoadedScenes();

            bool handled = false;
            handled |= TrySkipOpening();
            handled |= TryEnterMission1();
            handled |= TryEnterMission2();
            handled |= TryEnterMission3();
            handled |= TryEnterMission4();
            handled |= TryEnterMission5();
            handled |= TrySkipEnding();

            if (!handled)
            {
                Debug.Log("GlobalSceneHotkeyController: W pressed, but no skippable mission segment was found.");
            }
        }

        private static bool TrySkipOpening()
        {
            bool handled = false;
            foreach (PipelineManager pipelineManager in FindObjectsOfType<PipelineManager>(true))
            {
                if (pipelineManager == null)
                {
                    continue;
                }

                pipelineManager.SkipOpeningVideo();
                handled = true;
            }

            foreach (OpeningVideoPlayer openingVideoPlayer in FindObjectsOfType<OpeningVideoPlayer>(true))
            {
                if (openingVideoPlayer == null)
                {
                    continue;
                }

                openingVideoPlayer.SkipPlayback();
                handled = true;
            }

            return handled;
        }

        private static bool TryEnterMission1()
        {
            bool handled = false;
            foreach (Mission1Controller controller in FindObjectsOfType<Mission1Controller>(true))
            {
                controller.SkipToInteraction();
                handled = true;
            }

            return handled;
        }

        private static bool TryEnterMission2()
        {
            bool handled = false;
            foreach (Mission2StarMeshIntroAnimator controller in FindObjectsOfType<Mission2StarMeshIntroAnimator>(true))
            {
                controller.SkipToInteraction();
                handled = true;
            }

            return handled;
        }

        private static bool TryEnterMission3()
        {
            bool handled = false;
            foreach (Mission3HologramDisplayLoader controller in FindObjectsOfType<Mission3HologramDisplayLoader>(true))
            {
                controller.SkipToInteraction();
                handled = true;
            }

            foreach (HologramVideoPanelPlayer videoPanelPlayer in FindObjectsOfType<HologramVideoPanelPlayer>(true))
            {
                videoPanelPlayer.SkipPlaybackAndEnableInteraction();
                handled = true;
            }

            return handled;
        }

        private static bool TryEnterMission4()
        {
            bool handled = false;
            foreach (Mission4Controller controller in FindObjectsOfType<Mission4Controller>(true))
            {
                controller.SkipToInteraction();
                handled = true;
            }

            return handled;
        }

        private static bool TryEnterMission5()
        {
            bool handled = false;
            foreach (Mission5Controller controller in FindObjectsOfType<Mission5Controller>(true))
            {
                controller.SkipToInteraction();
                handled = true;
            }

            return handled;
        }

        private static bool TrySkipEnding()
        {
            bool handled = false;
            foreach (EndingVideoSequenceController controller in FindObjectsOfType<EndingVideoSequenceController>(true))
            {
                controller.SkipToEndingModel();
                handled = true;
            }

            return handled;
        }

        private static void StopPlaybackInLoadedScenes()
        {
            foreach (OpeningVideoPlayer player in FindObjectsOfType<OpeningVideoPlayer>(true))
            {
                player.SkipPlayback();
            }

            foreach (FullscreenStreamingVideoPlayer player in FindObjectsOfType<FullscreenStreamingVideoPlayer>(true))
            {
                player.SkipPlayback();
            }

            foreach (EndingHologramVideoPlayer player in FindObjectsOfType<EndingHologramVideoPlayer>(true))
            {
                player.SkipPlayback();
            }

            foreach (HologramVideoPanelPlayer player in FindObjectsOfType<HologramVideoPanelPlayer>(true))
            {
                player.SkipPlaybackAndEnableInteraction();
            }

            foreach (VideoPlayer player in FindObjectsOfType<VideoPlayer>(true))
            {
                if (player != null)
                {
                    player.Stop();
                }
            }
        }

        private static void StopNarrationInLoadedScenes()
        {
            foreach (NarrationSubtitleSequencePlayer narrationPlayer in FindObjectsOfType<NarrationSubtitleSequencePlayer>(true))
            {
                narrationPlayer.StopPlayback();
            }

            foreach (AudioSource audioSource in FindObjectsOfType<AudioSource>(true))
            {
                if (audioSource != null && audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
            }
        }
    }
}
