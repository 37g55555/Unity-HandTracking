using System.Collections;
using UnityEngine;

namespace ShadowPrototype
{
    public class SoftWhiteCirclePlaneScaleAnimator : MonoBehaviour
    {
        private const float StartupScaleInDurationSeconds = 2.0f;

        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private Transform targetPlane;
        [SerializeField] private Vector2 targetScaleXZ = new Vector2(8.0f, 4.0f);
        [SerializeField, Min(0.0f)] private float delaySeconds = 2.0f;
        [SerializeField, Min(0.01f)] private float durationSeconds = 4.0f;
        [SerializeField] private AnimationCurve scaleCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.0f, 0.0f, 0.0f),
            new Keyframe(0.28f, 0.05f, 0.18f, 0.18f),
            new Keyframe(0.72f, 0.86f, 1.6f, 1.6f),
            new Keyframe(1.0f, 1.0f, 0.0f, 0.0f));
        [SerializeField] private bool resetToInitialScaleOnEnable = true;

        private Vector3 initialScale;
        private Coroutine scaleRoutine;

        private void Awake()
        {
            if (targetPlane == null)
            {
                targetPlane = transform;
            }

            initialScale = targetPlane.localScale;
        }

        private void OnEnable()
        {
            if (resetToInitialScaleOnEnable && targetPlane != null)
            {
                targetPlane.localScale = Vector3.zero;
                StartScaleRoutine(ScalePlaneFromCurrentRoutine(initialScale, 0.0f, StartupScaleInDurationSeconds));
            }

            if (stateManager != null)
            {
                stateManager.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged -= HandleStateChanged;
            }

            if (scaleRoutine != null)
            {
                StopScaleRoutine();
            }
        }

        private void HandleStateChanged(GameStateManager.PipelineState currentState)
        {
            if (currentState != GameStateManager.PipelineState.MediaPipeTracking)
            {
                return;
            }

            if (targetPlane == null)
            {
                return;
            }

            StartScaleRoutine(ScalePlaneToTargetScaleRoutine());
        }

        private IEnumerator ScalePlaneToTargetScaleRoutine()
        {
            if (delaySeconds > 0.0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            Vector3 from = targetPlane.localScale;
            Vector3 to = new Vector3(targetScaleXZ.x, from.y, targetScaleXZ.y);
            yield return ScalePlaneRoutine(from, to, durationSeconds);
            scaleRoutine = null;
        }

        private IEnumerator ScalePlaneFromCurrentRoutine(Vector3 to, float delay, float duration)
        {
            if (delay > 0.0f)
            {
                yield return new WaitForSeconds(delay);
            }

            yield return ScalePlaneRoutine(targetPlane.localScale, to, duration);
            scaleRoutine = null;
        }

        private IEnumerator ScalePlaneRoutine(Vector3 from, Vector3 to, float duration)
        {
            float elapsed = 0.0f;
            float safeDuration = Mathf.Max(0.01f, duration);

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float progress = EvaluateScaleProgress(t);
                targetPlane.localScale = Vector3.LerpUnclamped(from, to, progress);
                yield return null;
            }

            targetPlane.localScale = to;
        }

        private void StartScaleRoutine(IEnumerator routine)
        {
            StopScaleRoutine();
            scaleRoutine = StartCoroutine(routine);
        }

        private void StopScaleRoutine()
        {
            if (scaleRoutine == null)
            {
                return;
            }

            StopCoroutine(scaleRoutine);
            scaleRoutine = null;
        }

        private float EvaluateScaleProgress(float normalizedTime)
        {
            if (scaleCurve == null || scaleCurve.length == 0)
            {
                return Mathf.SmoothStep(0.0f, 1.0f, normalizedTime);
            }

            return Mathf.Clamp01(scaleCurve.Evaluate(normalizedTime));
        }

        public float EvaluateScaleCurve(float normalizedTime)
        {
            return EvaluateScaleProgress(normalizedTime);
        }
    }
}
