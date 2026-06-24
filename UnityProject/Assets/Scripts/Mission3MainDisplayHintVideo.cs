using UnityEngine;

namespace ShadowPrototype
{
    public sealed class Mission3MainDisplayHintVideo : MonoBehaviour
    {
        [SerializeField] private GameObject hintVideoObject;
        [SerializeField] private TutorialVideoPlayer tutorialVideoPlayer;
        [SerializeField] private bool hideOnAwake = true;

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
    }
}
