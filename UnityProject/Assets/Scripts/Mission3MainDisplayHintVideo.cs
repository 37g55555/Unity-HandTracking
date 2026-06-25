using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace ShadowPrototype
{
    public sealed class Mission3MainDisplayHintVideo : MonoBehaviour
    {
        public enum InstructionVideo
        {
            None = 0,
            Turn5 = 1,
            Swipe = 2
        }

        [SerializeField] private GameObject hintVideoObject;
        [SerializeField] private TutorialVideoPlayer tutorialVideoPlayer;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField] private VideoClip turn5VideoClip;
        [SerializeField] private VideoClip swipeVideoClip;

        private void Awake()
        {
            ResolveReferences();

            if (hideOnAwake)
            {
                Hide();
            }
        }

        public void Show()
        {
            ResolveReferences();

            if (hintVideoObject == null)
            {
                return;
            }

            if (hintVideoObject.activeSelf)
            {
                hintVideoObject.SetActive(false);
            }

            hintVideoObject.SetActive(true);
        }

        public void Hide()
        {
            ResolveReferences();

            if (hintVideoObject != null)
            {
                hintVideoObject.SetActive(false);
            }
        }

        public static void ShowFirstAvailable()
        {
            FindFirstAvailable()?.Show();
        }

        public static void HideFirstAvailable()
        {
            FindFirstAvailable()?.Hide();
        }

        public static bool HasVisibleInstruction()
        {
            Mission3MainDisplayHintVideo player = FindFirstAvailable();
            return player != null && player.IsVisible();
        }

        public static IEnumerator PlayInstructionVideoAndWaitRoutine(InstructionVideo instructionVideo)
        {
            if (instructionVideo == InstructionVideo.None)
            {
                yield break;
            }

            Mission3MainDisplayHintVideo player = FindFirstAvailable();
            if (player == null)
            {
                yield break;
            }

            yield return player.PlayInstructionAndWaitRoutine(instructionVideo);
        }

        public static void ShowInstructionVideoLooping(InstructionVideo instructionVideo)
        {
            if (instructionVideo == InstructionVideo.None)
            {
                return;
            }

            FindFirstAvailable()?.ShowInstructionLooping(instructionVideo);
        }

        private static Mission3MainDisplayHintVideo FindFirstAvailable()
        {
            Mission3MainDisplayHintVideo[] players = Resources.FindObjectsOfTypeAll<Mission3MainDisplayHintVideo>();
            for (int i = 0; i < players.Length; i++)
            {
                Mission3MainDisplayHintVideo player = players[i];
                if (player != null && player.gameObject.scene.IsValid())
                {
                    return player;
                }
            }

            return null;
        }

        private bool IsVisible()
        {
            ResolveReferences();
            return hintVideoObject != null && hintVideoObject.activeInHierarchy;
        }

        private void ResolveReferences()
        {
            if (hintVideoObject == null && tutorialVideoPlayer != null)
            {
                hintVideoObject = tutorialVideoPlayer.gameObject;
            }

            if (tutorialVideoPlayer == null && hintVideoObject != null)
            {
                tutorialVideoPlayer = hintVideoObject.GetComponentInChildren<TutorialVideoPlayer>(true);
            }
        }

        private IEnumerator PlayInstructionAndWaitRoutine(InstructionVideo instructionVideo)
        {
            ResolveReferences();

            VideoClip clip = ResolveInstructionClip(instructionVideo);
            if (clip == null || hintVideoObject == null || tutorialVideoPlayer == null)
            {
                yield break;
            }

            if (hintVideoObject.activeSelf)
            {
                hintVideoObject.SetActive(false);
            }

            hintVideoObject.SetActive(true);
            yield return tutorialVideoPlayer.PlayClipAndWaitRoutine(clip);
            Hide();
        }

        private void ShowInstructionLooping(InstructionVideo instructionVideo)
        {
            ResolveReferences();

            VideoClip clip = ResolveInstructionClip(instructionVideo);
            if (clip == null || hintVideoObject == null || tutorialVideoPlayer == null)
            {
                return;
            }

            if (!hintVideoObject.activeSelf)
            {
                hintVideoObject.SetActive(true);
            }

            tutorialVideoPlayer.PlayClipLooping(clip);
        }

        private VideoClip ResolveInstructionClip(InstructionVideo instructionVideo)
        {
            switch (instructionVideo)
            {
                case InstructionVideo.Turn5:
                    return turn5VideoClip;
                case InstructionVideo.Swipe:
                    return swipeVideoClip != null ? swipeVideoClip : tutorialVideoPlayer?.CurrentClip;
                default:
                    return null;
            }
        }
    }
}
