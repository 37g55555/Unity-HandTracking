using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    [DisallowMultipleComponent]
    public sealed class EndingVideoSequenceController : MonoBehaviour
    {
        [SerializeField] private FullscreenStreamingVideoPlayer metamorphosisVideoPlayer;
        [SerializeField] private string metamorphosisVideoRelativePath = "Videos/6 Metamorphosis.mp4";
        [SerializeField] private string connectVideoRelativePath = "Videos/7 Ending_connect.mp4";
        [SerializeField] private string postInteractionScreenVideoRelativePath = "Videos/ending_screen.mp4";
        [SerializeField] private string postInteractionHologramVideoRelativePath = "Videos/ending_hologram.mp4";
        [SerializeField] private EndingHologramVideoPlayer postInteractionHologramVideoPlayer;
        [SerializeField] private bool playFullscreenIntroVideos = true;
        [SerializeField] private bool loadHologramSceneAfterIntroVideos;
        [SerializeField] private string hologramSceneName = "Ending_H";
        [SerializeField] private bool playHologramSequenceInThisScene = true;
        [SerializeField] private bool playHologramIntroVideo = true;
        [SerializeField] private EndingHologramVideoPlayer endingHologramVideoPlayer;
        [SerializeField] private EndingHologramModelPresenter endingHologramModelPresenter;
        [SerializeField] private bool playOnStart = true;

        private Coroutine sequenceRoutine;
        private Coroutine postInteractionRoutine;

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        public void Play()
        {
            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
            }

            sequenceRoutine = StartCoroutine(PlaySequenceRoutine());
        }

        public void SkipToEndingModel()
        {
            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
                sequenceRoutine = null;
            }

            ResolveReferences();
            metamorphosisVideoPlayer?.SkipPlayback();

            if (loadHologramSceneAfterIntroVideos)
            {
                sequenceRoutine = StartCoroutine(LoadHologramSceneAndClearRoutine());
                return;
            }

            if (playHologramSequenceInThisScene)
            {
                endingHologramVideoPlayer?.SkipPlayback();
                if (endingHologramModelPresenter != null)
                {
                    endingHologramModelPresenter.Show();
                    endingHologramModelPresenter.SetPokeInputEnabled(true);
                }
            }
        }

        public bool HasFullscreenVideoPlayer
        {
            get
            {
                ResolveReferences();
                return metamorphosisVideoPlayer != null;
            }
        }

        public void PlayPostInteractionVideos(EndingHologramVideoPlayer hologramVideoPlayer = null)
        {
            ResolveReferences();

            if (postInteractionRoutine != null)
            {
                StopCoroutine(postInteractionRoutine);
            }

            postInteractionRoutine = StartCoroutine(PlayPostInteractionVideosRoutine(hologramVideoPlayer));
        }

        private IEnumerator PlaySequenceRoutine()
        {
            ResolveReferences();

            if (playHologramSequenceInThisScene && endingHologramModelPresenter != null)
            {
                endingHologramModelPresenter.Show();
                endingHologramModelPresenter.SetPokeInputEnabled(false);
            }
            else if (playHologramSequenceInThisScene)
            {
                Debug.LogWarning("EndingVideoSequenceController: ending hologram model presenter is missing.");
            }

            if (playFullscreenIntroVideos)
            {
                if (metamorphosisVideoPlayer != null)
                {
                    yield return metamorphosisVideoPlayer.PlayAndWaitRoutine(metamorphosisVideoRelativePath);
                    Debug.Log("EndingVideoSequenceController: metamorphosis video finished; starting connect video.");
                }
                else
                {
                    Debug.LogWarning("EndingVideoSequenceController: metamorphosis video player is missing; starting connect video.");
                }

                if (metamorphosisVideoPlayer != null)
                {
                    yield return metamorphosisVideoPlayer.PlayAndWaitRoutine(connectVideoRelativePath);
                    Debug.Log("EndingVideoSequenceController: connect video finished.");
                }
                else
                {
                    Debug.LogWarning("EndingVideoSequenceController: fullscreen video player is missing.");
                }
            }

            if (loadHologramSceneAfterIntroVideos)
            {
                yield return LoadHologramSceneRoutine();
                sequenceRoutine = null;
                yield break;
            }

            if (playHologramSequenceInThisScene && playHologramIntroVideo && endingHologramVideoPlayer != null)
            {
                yield return endingHologramVideoPlayer.PlayAndWaitRoutine();
                Debug.Log("EndingVideoSequenceController: ending hologram video finished; showing hologram model.");
            }
            else if (playHologramSequenceInThisScene && playHologramIntroVideo)
            {
                Debug.LogWarning("EndingVideoSequenceController: ending hologram video player is missing.");
            }

            if (playHologramSequenceInThisScene && endingHologramModelPresenter != null)
            {
                endingHologramModelPresenter.SetPokeInputEnabled(true);
            }
            else if (playHologramSequenceInThisScene)
            {
                Debug.LogWarning("EndingVideoSequenceController: ending hologram model presenter is missing.");
            }

            sequenceRoutine = null;
        }

        private IEnumerator PlayPostInteractionVideosRoutine(EndingHologramVideoPlayer hologramVideoPlayer)
        {
            EndingHologramVideoPlayer resolvedHologramVideoPlayer =
                hologramVideoPlayer != null ? hologramVideoPlayer : ResolvePostInteractionHologramVideoPlayer();

            Coroutine screenVideoRoutine = null;
            Coroutine hologramVideoRoutine = null;

            if (metamorphosisVideoPlayer != null)
            {
                screenVideoRoutine = StartCoroutine(
                    metamorphosisVideoPlayer.PlayAndWaitRoutine(postInteractionScreenVideoRelativePath, true));
            }
            else
            {
                Debug.LogWarning("EndingVideoSequenceController: fullscreen video player is missing for ending_screen.");
            }

            if (resolvedHologramVideoPlayer != null)
            {
                hologramVideoRoutine = StartCoroutine(
                    resolvedHologramVideoPlayer.PlayAndWaitRoutine(postInteractionHologramVideoRelativePath));
            }
            else
            {
                Debug.LogWarning("EndingVideoSequenceController: hologram video player is missing for ending_hologram.");
            }

            if (screenVideoRoutine != null)
            {
                yield return screenVideoRoutine;
            }

            if (hologramVideoRoutine != null)
            {
                yield return hologramVideoRoutine;
            }

            postInteractionRoutine = null;
        }

        private void ResolveReferences()
        {
            if (metamorphosisVideoPlayer == null)
            {
                metamorphosisVideoPlayer = GetComponent<FullscreenStreamingVideoPlayer>();
            }

            if (endingHologramVideoPlayer == null)
            {
                endingHologramVideoPlayer = GetComponent<EndingHologramVideoPlayer>();
            }

            if (endingHologramModelPresenter == null)
            {
                endingHologramModelPresenter = GetComponent<EndingHologramModelPresenter>();
            }
        }

        private EndingHologramVideoPlayer ResolvePostInteractionHologramVideoPlayer()
        {
            if (postInteractionHologramVideoPlayer != null)
            {
                return postInteractionHologramVideoPlayer;
            }

            if (endingHologramVideoPlayer != null)
            {
                postInteractionHologramVideoPlayer = endingHologramVideoPlayer;
                return postInteractionHologramVideoPlayer;
            }

            EndingHologramVideoPlayer[] videoPlayers = FindObjectsOfType<EndingHologramVideoPlayer>();
            if (videoPlayers.Length > 0)
            {
                postInteractionHologramVideoPlayer = videoPlayers[0];
            }

            return postInteractionHologramVideoPlayer;
        }

        private IEnumerator LoadHologramSceneAndClearRoutine()
        {
            yield return LoadHologramSceneRoutine();
            sequenceRoutine = null;
        }

        private IEnumerator LoadHologramSceneRoutine()
        {
            if (string.IsNullOrWhiteSpace(hologramSceneName))
            {
                yield break;
            }

            int displayIndex = DisplayRoutingSettings.ResolveUnityDisplayIndex(
                DisplayRoutingSettings.HologramUnityDisplayIndex);
            DisplayRoutingSettings.ActivateUnityDisplay(displayIndex);

            Scene hologramScene = SceneManager.GetSceneByName(hologramSceneName);
            if (!hologramScene.IsValid() || !hologramScene.isLoaded)
            {
                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(hologramSceneName, LoadSceneMode.Additive);
                if (loadOperation == null)
                {
                    Debug.LogWarning($"EndingVideoSequenceController: hologram scene could not be loaded: {hologramSceneName}");
                    yield break;
                }

                while (!loadOperation.isDone)
                {
                    yield return null;
                }
            }

            ApplyHologramTargetDisplay(displayIndex);
            yield return null;
            ApplyHologramTargetDisplay(displayIndex);
            Debug.Log($"EndingVideoSequenceController: loaded hologram scene additively: {hologramSceneName}");
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
