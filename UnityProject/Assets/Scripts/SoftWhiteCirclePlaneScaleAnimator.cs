using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public class SoftWhiteCirclePlaneScaleAnimator : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private Transform targetPlane;
        [SerializeField] private bool playOnStart;
        [SerializeField] private Vector2 targetScaleXZ = new Vector2(8.0f, 4.0f);
        [SerializeField, Min(0.0f)] private float delaySeconds;
        [SerializeField, Min(0.01f)] private float durationSeconds = 5.0f;
        [SerializeField] private bool destroyTargetPlaneOnTargetScaleReached = true;
        [SerializeField] private AnimationCurve scaleCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.0f, 0.0f, 0.0f),
            new Keyframe(0.28f, 0.05f, 0.18f, 0.18f),
            new Keyframe(0.72f, 0.86f, 1.6f, 1.6f),
            new Keyframe(1.0f, 1.0f, 0.0f, 0.0f));

        private Coroutine scaleRoutine;
        private Coroutine deferredDestroyRoutine;
        private GameObject deferredDestroyTarget;
        private bool hasPlayed;

        public bool HasCompleted { get; private set; }

        private void Awake()
        {
            if (targetPlane == null)
            {
                targetPlane = transform;
            }

        }

        private void OnEnable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged += HandleStateChanged;
            }
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayScaleAnimation();
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

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoadedForDeferredDestroy;
        }

        private void HandleStateChanged(GameStateManager.PipelineState currentState)
        {
            if (currentState != GameStateManager.PipelineState.Mission1)
            {
                return;
            }

            if (targetPlane == null)
            {
                return;
            }

            PlayScaleAnimation();
        }

        private void PlayScaleAnimation()
        {
            if (hasPlayed || targetPlane == null)
            {
                return;
            }

            hasPlayed = true;
            StartScaleRoutine(ScalePlaneToTargetScaleRoutine());
        }

        public void Play()
        {
            PlayScaleAnimation();
        }

        public IEnumerator PlayAndWaitRoutine()
        {
            PlayScaleAnimation();
            while (scaleRoutine != null)
            {
                yield return null;
            }
        }

        public void SetDestroyTargetPlaneOnTargetScaleReached(bool shouldDestroy)
        {
            destroyTargetPlaneOnTargetScaleReached = shouldDestroy;
        }

        public void KeepTargetPlaneUntilNextSceneFirstFrame()
        {
            if (targetPlane == null)
            {
                return;
            }

            destroyTargetPlaneOnTargetScaleReached = false;
            deferredDestroyTarget = targetPlane.gameObject;
            deferredDestroyTarget.transform.SetParent(null, true);
            DontDestroyOnLoad(deferredDestroyTarget);

            SceneManager.sceneLoaded -= HandleSceneLoadedForDeferredDestroy;
            SceneManager.sceneLoaded += HandleSceneLoadedForDeferredDestroy;
        }

        private void HandleSceneLoadedForDeferredDestroy(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= HandleSceneLoadedForDeferredDestroy;

            if (deferredDestroyRoutine != null)
            {
                StopCoroutine(deferredDestroyRoutine);
            }

            deferredDestroyRoutine = StartCoroutine(DestroyDeferredTargetAfterFirstFrameRoutine());
        }

        private IEnumerator DestroyDeferredTargetAfterFirstFrameRoutine()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            if (deferredDestroyTarget != null)
            {
                Destroy(deferredDestroyTarget);
            }

            deferredDestroyTarget = null;
            deferredDestroyRoutine = null;
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
            HasCompleted = true;
            DestroyTargetPlaneIfNeeded();
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

        private void DestroyTargetPlaneIfNeeded()
        {
            if (!destroyTargetPlaneOnTargetScaleReached || targetPlane == null)
            {
                return;
            }

            Destroy(targetPlane.gameObject);
        }
    }
}
