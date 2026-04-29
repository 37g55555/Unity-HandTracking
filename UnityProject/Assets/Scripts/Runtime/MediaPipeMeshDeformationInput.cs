using UnityEngine;

namespace ShadowPrototype
{
    public class MediaPipeMeshDeformationInput : MonoBehaviour
    {
        public enum InteractionMode
        {
            None,
            Hover,
            Push,
            Pull,
            Tear
        }

        private const int LandmarksPerHand = 21;
        private const int ThumbTipIndex = 4;
        private const int IndexTipIndex = 8;
        private const int MiddleTipIndex = 12;

        [SerializeField] private ShadowDeformer targetDeformer;
        [SerializeField] private HandLandmarkUdpReceiver handReceiver;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private int controllingHandIndex;
        [SerializeField] private float trackedFrameWidth = 1280.0f;
        [SerializeField] private float trackedFrameHeight = 720.0f;

        [Header("Push")]
        [SerializeField] private bool enablePush = true;
        [SerializeField] private float pushRadius = 0.16f;
        [SerializeField] private float pushStrengthPerSecond = 0.65f;

        [Header("Pull")]
        [SerializeField] private bool enablePull = true;
        [SerializeField] private float pinchDistanceThresholdPixels = 70.0f;
        [SerializeField] private float pullRadius = 0.22f;
        [SerializeField] private float pullStrength = 1.0f;
        [SerializeField] private float maxPullDeltaPerFrame = 0.06f;

        [Header("Tear")]
        [SerializeField] private bool enableTear = true;
        [SerializeField] private float tearFingerDistanceThresholdPixels = 150.0f;
        [SerializeField] private float tearSpreadDeltaThresholdPixels = 28.0f;
        [SerializeField] private float tearWidth = 0.09f;
        [SerializeField] private float tearSeparation = 0.05f;
        [SerializeField] private float tearCooldownSeconds = 0.85f;

        private bool wasPinchingLastFrame;
        private bool hasPreviousGrabPoint;
        private Vector2 previousGrabPointLocal;
        private float previousScissorDistancePixels;
        private float nextAllowedTearTime;

        public InteractionMode CurrentMode { get; private set; }
        public bool HasProjectedPoints { get; private set; }
        public Vector2 ThumbLocalPoint { get; private set; }
        public Vector2 IndexLocalPoint { get; private set; }
        public Vector2 MiddleLocalPoint { get; private set; }
        public Vector2 GrabLocalPoint { get; private set; }
        public Vector3 ThumbWorldPoint { get; private set; }
        public Vector3 IndexWorldPoint { get; private set; }
        public Vector3 MiddleWorldPoint { get; private set; }
        public Vector3 GrabWorldPoint { get; private set; }
        public float PushRadiusLocal => pushRadius;
        public float PullRadiusLocal => pullRadius;
        public float TearWidthLocal => tearWidth;

        public void Configure(ShadowDeformer deformer, HandLandmarkUdpReceiver receiver)
        {
            targetDeformer = deformer;
            handReceiver = receiver;
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            ResolveDependencies();
            if (targetDeformer == null || handReceiver == null || !targetDeformer.HasMesh)
            {
                ResetGestureState();
                return;
            }

            if (!handReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                ResetGestureState();
                return;
            }

            if (!TryGetHandPoint(landmarks, controllingHandIndex, ThumbTipIndex, out Vector2 thumbTracked) ||
                !TryGetHandPoint(landmarks, controllingHandIndex, IndexTipIndex, out Vector2 indexTracked) ||
                !TryGetHandPoint(landmarks, controllingHandIndex, MiddleTipIndex, out Vector2 middleTracked))
            {
                ResetGestureState();
                return;
            }

            if (!TryProjectTrackedPointToLocal(thumbTracked, out Vector2 thumbLocal) ||
                !TryProjectTrackedPointToLocal(indexTracked, out Vector2 indexLocal) ||
                !TryProjectTrackedPointToLocal(middleTracked, out Vector2 middleLocal))
            {
                ResetGestureState();
                return;
            }

            ThumbLocalPoint = thumbLocal;
            IndexLocalPoint = indexLocal;
            MiddleLocalPoint = middleLocal;
            GrabLocalPoint = (thumbLocal + indexLocal) * 0.5f;
            ThumbWorldPoint = targetDeformer.transform.TransformPoint(new Vector3(thumbLocal.x, thumbLocal.y, 0.0f));
            IndexWorldPoint = targetDeformer.transform.TransformPoint(new Vector3(indexLocal.x, indexLocal.y, 0.0f));
            MiddleWorldPoint = targetDeformer.transform.TransformPoint(new Vector3(middleLocal.x, middleLocal.y, 0.0f));
            GrabWorldPoint = targetDeformer.transform.TransformPoint(new Vector3(GrabLocalPoint.x, GrabLocalPoint.y, 0.0f));
            HasProjectedPoints = true;

            bool indexInsideMesh = targetDeformer.ContainsLocalPoint(indexLocal);
            bool middleInsideMesh = targetDeformer.ContainsLocalPoint(middleLocal);
            bool grabInsideMesh = targetDeformer.ContainsLocalPoint(GrabLocalPoint);

            float pinchDistancePixels = Vector2.Distance(thumbTracked, indexTracked);
            bool isPinching = enablePull && pinchDistancePixels <= pinchDistanceThresholdPixels;
            bool tearPoseCandidate = enableTear &&
                                     !isPinching &&
                                     indexInsideMesh &&
                                     middleInsideMesh &&
                                     Vector2.Distance(indexTracked, middleTracked) >= tearFingerDistanceThresholdPixels;

            CurrentMode = InteractionMode.None;
            if (grabInsideMesh && isPinching)
            {
                CurrentMode = InteractionMode.Pull;
            }
            else if (tearPoseCandidate)
            {
                CurrentMode = InteractionMode.Tear;
            }
            else if (indexInsideMesh)
            {
                CurrentMode = InteractionMode.Push;
            }
            else if (HasProjectedPoints)
            {
                CurrentMode = InteractionMode.Hover;
            }

            if (enablePush && !isPinching && indexInsideMesh)
            {
                float pushAmount = pushStrengthPerSecond * Time.deltaTime;
                targetDeformer.ApplyPush(indexLocal, pushRadius, pushAmount);
            }

            if (isPinching && (grabInsideMesh || wasPinchingLastFrame))
            {
                if (hasPreviousGrabPoint)
                {
                    Vector2 pullDelta = Vector2.ClampMagnitude(GrabLocalPoint - previousGrabPointLocal, maxPullDeltaPerFrame);
                    if (pullDelta.sqrMagnitude > 0.0f)
                    {
                        targetDeformer.ApplyPull(GrabLocalPoint, pullDelta, pullRadius, pullStrength);
                    }
                }

                previousGrabPointLocal = GrabLocalPoint;
                hasPreviousGrabPoint = true;
            }
            else
            {
                hasPreviousGrabPoint = false;
            }

            if (enableTear && !isPinching)
            {
                float scissorDistancePixels = Vector2.Distance(indexTracked, middleTracked);
                float spreadDeltaPixels = scissorDistancePixels - previousScissorDistancePixels;
                bool canTear = Time.time >= nextAllowedTearTime &&
                               tearPoseCandidate &&
                               spreadDeltaPixels >= tearSpreadDeltaThresholdPixels;

                if (canTear && targetDeformer.ApplyTear(indexLocal, middleLocal, tearWidth, tearSeparation))
                {
                    nextAllowedTearTime = Time.time + tearCooldownSeconds;
                }

                previousScissorDistancePixels = scissorDistancePixels;
            }
            else
            {
                previousScissorDistancePixels = 0.0f;
            }

            wasPinchingLastFrame = isPinching;
        }

        private void ResetGestureState()
        {
            CurrentMode = InteractionMode.None;
            HasProjectedPoints = false;
            wasPinchingLastFrame = false;
            hasPreviousGrabPoint = false;
            previousScissorDistancePixels = 0.0f;
        }

        private bool TryGetHandPoint(Vector3[] landmarks, int handIndex, int landmarkIndex, out Vector2 trackedPoint)
        {
            trackedPoint = Vector2.zero;

            int startIndex = handIndex * LandmarksPerHand;
            int absoluteIndex = startIndex + landmarkIndex;
            if (landmarks == null || absoluteIndex < 0 || absoluteIndex >= landmarks.Length)
            {
                return false;
            }

            Vector3 landmark = landmarks[absoluteIndex];
            trackedPoint = new Vector2(landmark.x, landmark.y);
            return true;
        }

        private bool TryProjectTrackedPointToLocal(Vector2 trackedPoint, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            Camera camera = ResolveCamera();
            if (camera == null || trackedFrameWidth <= 0.0f || trackedFrameHeight <= 0.0f)
            {
                return false;
            }

            Vector3 viewportPoint = new Vector3(
                Mathf.Clamp01(trackedPoint.x / trackedFrameWidth),
                Mathf.Clamp01(trackedPoint.y / trackedFrameHeight),
                0.0f);

            Ray ray = camera.ViewportPointToRay(viewportPoint);
            Plane meshPlane = new Plane(targetDeformer.transform.forward, targetDeformer.transform.position);
            if (!meshPlane.Raycast(ray, out float enter))
            {
                return false;
            }

            Vector3 worldPoint = ray.GetPoint(enter);
            Vector3 localPoint3 = targetDeformer.transform.InverseTransformPoint(worldPoint);
            localPoint = new Vector2(localPoint3.x, localPoint3.y);
            return true;
        }

        private Camera ResolveCamera()
        {
            if (targetCamera != null)
            {
                return targetCamera;
            }

            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            }

            return targetCamera;
        }

        private void ResolveDependencies()
        {
            if (targetDeformer == null)
            {
                targetDeformer = UnityEngine.Object.FindAnyObjectByType<ShadowDeformer>();
            }

            if (handReceiver == null)
            {
                handReceiver = UnityEngine.Object.FindAnyObjectByType<HandLandmarkUdpReceiver>();
            }

            ResolveCamera();
        }
    }
}
