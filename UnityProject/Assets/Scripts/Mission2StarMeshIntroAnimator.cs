using System.Collections;
using UnityEngine;

namespace ShadowPrototype
{
    public sealed class Mission2StarMeshIntroAnimator : MonoBehaviour
    {
        [SerializeField] private Transform targetTransform;
        [SerializeField] private Vector2 targetPosition = new Vector2(-4.0f, -3.0f);
        [SerializeField, Min(0.0f)] private float targetUniformScale = 1.0f;
        [SerializeField, Min(0.0f)] private float durationSeconds = 2.0f;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool setMission2StateOnStart = true;

        private Coroutine animationRoutine;

        private void Start()
        {
            if (setMission2StateOnStart)
            {
                FindObjectOfType<GameStateManager>()?.SetState(GameStateManager.PipelineState.Mission2);
            }

            if (playOnStart)
            {
                Play();
            }
        }

        public void Play()
        {
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
            }

            animationRoutine = StartCoroutine(AnimateWhenTargetIsReady());
        }

        private IEnumerator AnimateWhenTargetIsReady()
        {
            float waitDeadline = Time.unscaledTime + 1.0f;
            while (ResolveTargetTransform() == null && Time.unscaledTime < waitDeadline)
            {
                yield return null;
            }

            Transform target = ResolveTargetTransform();
            if (target == null)
            {
                Debug.LogWarning("Mission2StarMeshIntroAnimator: target star transform was not found.");
                animationRoutine = null;
                yield break;
            }

            yield return AnimateTargetRoot(target);
            animationRoutine = null;
        }

        private IEnumerator AnimateTargetRoot(Transform target)
        {
            Vector3 startPosition = target.position;
            Vector3 startScale = target.localScale;
            Vector3 endPosition = new Vector3(targetPosition.x, targetPosition.y, startPosition.z);
            Vector3 endScale = Vector3.one * targetUniformScale;

            if (durationSeconds <= 0.0f)
            {
                target.position = endPosition;
                target.localScale = endScale;
                yield break;
            }

            float elapsed = 0.0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / durationSeconds);
                float easedT = Mathf.SmoothStep(0.0f, 1.0f, t);
                target.position = Vector3.LerpUnclamped(startPosition, endPosition, easedT);
                target.localScale = Vector3.LerpUnclamped(startScale, endScale, easedT);
                yield return null;
            }

            target.position = endPosition;
            target.localScale = endScale;
        }

        private Transform ResolveTargetTransform()
        {
            if (targetTransform != null)
            {
                return targetTransform;
            }

            GameObject shadowStar = GameObject.Find("ShadowStar");
            if (shadowStar != null)
            {
                targetTransform = shadowStar.transform;
                return targetTransform;
            }

            GameObject mission2Star = GameObject.Find("Mission2Star");
            if (mission2Star != null)
            {
                targetTransform = mission2Star.transform;
                return targetTransform;
            }

            Mission2StarShape mission2StarShape = FindObjectOfType<Mission2StarShape>();
            if (mission2StarShape != null)
            {
                targetTransform = mission2StarShape.transform;
            }

            return targetTransform;
        }
    }
}
