using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class Mission4DoorTransition : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform shadowStar;
        [SerializeField] private Transform door;
        [SerializeField] private string fallbackDoorName = "Door";

        [Header("Transition")]
        [SerializeField] private string nextSceneName = "Mission5";
        [SerializeField, Min(0.0f)] private float contactPadding = 0.02f;
        [SerializeField, Min(0.0f)] private float fallbackTouchDistance = 0.35f;

        private Renderer[] shadowRenderers;
        private Renderer[] doorRenderers;
        private bool transitionStarted;

        private void Awake()
        {
            if (shadowStar == null)
            {
                shadowStar = transform;
            }

            ResolveReferences();
        }

        private void LateUpdate()
        {
            if (transitionStarted)
            {
                return;
            }

            ResolveReferences();
            if (shadowStar == null || door == null)
            {
                return;
            }

            if (IsTouchingDoor())
            {
                transitionStarted = true;
                FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission5);

                if (!string.IsNullOrWhiteSpace(nextSceneName))
                {
                    SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
                }
            }
        }

        private void ResolveReferences()
        {
            if (door == null && !string.IsNullOrWhiteSpace(fallbackDoorName))
            {
                GameObject doorObject = GameObject.Find(fallbackDoorName);
                door = doorObject != null ? doorObject.transform : null;
            }

            if (shadowStar != null && (shadowRenderers == null || shadowRenderers.Length == 0))
            {
                shadowRenderers = shadowStar.GetComponentsInChildren<Renderer>(true);
            }

            if (door != null && (doorRenderers == null || doorRenderers.Length == 0))
            {
                doorRenderers = door.GetComponentsInChildren<Renderer>(true);
            }
        }

        private bool IsTouchingDoor()
        {
            if (TryGetBounds(shadowRenderers, out Bounds shadowBounds) &&
                TryGetBounds(doorRenderers, out Bounds doorBounds))
            {
                shadowBounds.Expand(contactPadding * 2.0f);
                doorBounds.Expand(contactPadding * 2.0f);
                return OverlapsXY(shadowBounds, doorBounds);
            }

            return Vector3.Distance(shadowStar.position, door.position) <= fallbackTouchDistance;
        }

        private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer current = renderers[i];
                if (current == null || !current.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = current.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(current.bounds);
                }
            }

            return hasBounds;
        }

        private static bool OverlapsXY(Bounds a, Bounds b)
        {
            return a.min.x <= b.max.x &&
                a.max.x >= b.min.x &&
                a.min.y <= b.max.y &&
                a.max.y >= b.min.y;
        }
    }
}
