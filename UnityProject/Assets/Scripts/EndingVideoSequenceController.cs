using System.Collections;
using UnityEngine;

namespace ShadowPrototype
{
    [DisallowMultipleComponent]
    public sealed class EndingVideoSequenceController : MonoBehaviour
    {
        [SerializeField] private FullscreenStreamingVideoPlayer metamorphosisVideoPlayer;
        [SerializeField] private FullscreenStreamingVideoPlayer connectVideoPlayer;
        [SerializeField] private EndingHologramVideoPlayer endingHologramVideoPlayer;
        [SerializeField] private EndingHologramModelPresenter endingHologramModelPresenter;
        [SerializeField] private bool playOnStart = true;

        private Coroutine sequenceRoutine;

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
            connectVideoPlayer?.SkipPlayback();
            endingHologramVideoPlayer?.SkipPlayback();
            endingHologramModelPresenter?.Show();
        }

        private IEnumerator PlaySequenceRoutine()
        {
            ResolveReferences();
            endingHologramModelPresenter?.Hide();

            if (metamorphosisVideoPlayer != null)
            {
                yield return metamorphosisVideoPlayer.PlayAndWaitRoutine();
                Debug.Log("EndingVideoSequenceController: metamorphosis video finished; starting connect video.");
            }
            else
            {
                Debug.LogWarning("EndingVideoSequenceController: metamorphosis video player is missing; starting connect video.");
            }

            if (connectVideoPlayer != null)
            {
                yield return connectVideoPlayer.PlayAndWaitRoutine();
                Debug.Log("EndingVideoSequenceController: connect video finished; starting hologram ending video.");
            }
            else
            {
                Debug.LogWarning("EndingVideoSequenceController: connect video player is missing; starting hologram ending video.");
            }

            if (endingHologramModelPresenter != null)
            {
                endingHologramModelPresenter.ShowSidePanelsOnly();
            }

            if (endingHologramVideoPlayer != null)
            {
                yield return endingHologramVideoPlayer.PlayAndWaitRoutine();
                Debug.Log("EndingVideoSequenceController: ending hologram video finished; showing hologram model.");
            }
            else
            {
                Debug.LogWarning("EndingVideoSequenceController: ending hologram video player is missing.");
            }

            if (endingHologramModelPresenter != null)
            {
                endingHologramModelPresenter.Show();
            }
            else
            {
                Debug.LogWarning("EndingVideoSequenceController: ending hologram model presenter is missing.");
            }

            sequenceRoutine = null;
        }

        private void ResolveReferences()
        {
            if (metamorphosisVideoPlayer == null)
            {
                metamorphosisVideoPlayer = GetComponent<FullscreenStreamingVideoPlayer>();
            }

            if (connectVideoPlayer == null)
            {
                FullscreenStreamingVideoPlayer[] fullscreenPlayers = GetComponentsInChildren<FullscreenStreamingVideoPlayer>(true);
                for (int i = 0; i < fullscreenPlayers.Length; i++)
                {
                    if (fullscreenPlayers[i] != null && fullscreenPlayers[i] != metamorphosisVideoPlayer)
                    {
                        connectVideoPlayer = fullscreenPlayers[i];
                        break;
                    }
                }
            }

            if (endingHologramVideoPlayer == null)
            {
                endingHologramVideoPlayer = GetComponent<EndingHologramVideoPlayer>();
            }

            if (endingHologramModelPresenter == null)
            {
                endingHologramModelPresenter = GetComponentInChildren<EndingHologramModelPresenter>(true);
            }

            if (endingHologramModelPresenter == null)
            {
                endingHologramModelPresenter = gameObject.AddComponent<EndingHologramModelPresenter>();
                Debug.Log("EndingVideoSequenceController: added missing ending hologram model presenter.");
            }
        }
    }
}
