using UnityEngine;

namespace ShadowPrototype
{
    public class MediaPipeScaleInput : MonoBehaviour
    {
        private const int LandmarksPerHand = 21;
        private const int WristIndex = 0;
        private const int IndexMcpIndex = 5;
        private const int MiddleMcpIndex = 9;
        private const int PinkyMcpIndex = 17;

        [SerializeField] private ShadowMeshRootController targetController;
        [SerializeField] private HandLandmarkUdpReceiver handReceiver;
        [SerializeField] private int controllingHandIndex;
        [SerializeField] private float trackedFrameWidth = 1280.0f;
        [SerializeField] private float trackedFrameHeight = 720.0f;

        [Header("Scale")]
        [SerializeField] private float minHandSpan = 120.0f;
        [SerializeField] private float maxHandSpan = 420.0f;
        [SerializeField] private bool invertScale;
        [SerializeField] private bool driveScale;

        [Header("Position")]
        [SerializeField] private bool drivePosition;
        [SerializeField] private bool invertX;
        [SerializeField] private bool invertY;

        [Header("Rotation")]
        [SerializeField] private bool driveRotation;
        [SerializeField] private float minPalmAngleDegrees = -140.0f;
        [SerializeField] private float maxPalmAngleDegrees = 40.0f;

        [Header("Smoothing")]
        [SerializeField] private float scaleSmoothingSpeed = 10.0f;
        [SerializeField] private float positionSmoothingSpeed = 12.0f;
        [SerializeField] private float rotationSmoothingSpeed = 12.0f;

        private bool hasInitializedPose;
        private float smoothedScale = 0.5f;
        private Vector2 smoothedPosition = new Vector2(0.5f, 0.5f);
        private float smoothedRotation = 0.5f;

        public void Configure(ShadowMeshRootController controller, HandLandmarkUdpReceiver receiver)
        {
            targetController = controller;
            handReceiver = receiver;
        }

        private void Awake()
        {
            ResolveDependencies();
            if (targetController != null)
            {
                smoothedScale = targetController.CurrentNormalizedScale;
                smoothedPosition = targetController.CurrentNormalizedPosition;
                smoothedRotation = targetController.CurrentNormalizedRotation;
            }
        }

        private void Update()
        {
            ResolveDependencies();
            if (targetController == null || handReceiver == null)
            {
                return;
            }

            if (!handReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                return;
            }

            if (!TryComputePose(landmarks, controllingHandIndex, out Vector2 nextPosition, out float nextScale, out float nextRotation))
            {
                return;
            }

            if (invertScale)
            {
                nextScale = 1.0f - nextScale;
            }

            if (!hasInitializedPose)
            {
                smoothedPosition = nextPosition;
                smoothedScale = nextScale;
                smoothedRotation = nextRotation;
                hasInitializedPose = true;
            }
            else
            {
                float positionBlend = 1.0f - Mathf.Exp(-positionSmoothingSpeed * Time.deltaTime);
                float scaleBlend = 1.0f - Mathf.Exp(-scaleSmoothingSpeed * Time.deltaTime);
                float rotationBlend = 1.0f - Mathf.Exp(-rotationSmoothingSpeed * Time.deltaTime);

                smoothedPosition = Vector2.Lerp(smoothedPosition, nextPosition, positionBlend);
                smoothedScale = Mathf.Lerp(smoothedScale, nextScale, scaleBlend);
                smoothedRotation = Mathf.Lerp(smoothedRotation, nextRotation, rotationBlend);
            }

            Vector2 appliedPosition = drivePosition ? smoothedPosition : targetController.CurrentNormalizedPosition;
            float appliedScale = driveScale ? smoothedScale : targetController.CurrentNormalizedScale;
            float appliedRotation = driveRotation ? smoothedRotation : targetController.CurrentNormalizedRotation;
            targetController.SetPoseNormalized(appliedPosition, appliedScale, appliedRotation);
        }

        private bool TryComputePose(
            Vector3[] landmarks,
            int handIndex,
            out Vector2 normalizedPosition,
            out float normalizedScale,
            out float normalizedRotation)
        {
            normalizedPosition = new Vector2(0.5f, 0.5f);
            normalizedScale = 0.5f;
            normalizedRotation = 0.5f;

            int startIndex = handIndex * LandmarksPerHand;
            int endIndex = startIndex + LandmarksPerHand;
            if (landmarks == null || landmarks.Length < endIndex)
            {
                return false;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            Vector2 centroid = Vector2.zero;

            for (int i = startIndex; i < endIndex; i++)
            {
                Vector3 landmark = landmarks[i];
                centroid += new Vector2(landmark.x, landmark.y);
                minX = Mathf.Min(minX, landmark.x);
                minY = Mathf.Min(minY, landmark.y);
                maxX = Mathf.Max(maxX, landmark.x);
                maxY = Mathf.Max(maxY, landmark.y);
            }

            centroid /= LandmarksPerHand;
            normalizedPosition = new Vector2(
                Mathf.Clamp01(centroid.x / Mathf.Max(trackedFrameWidth, 1.0f)),
                Mathf.Clamp01(centroid.y / Mathf.Max(trackedFrameHeight, 1.0f)));

            if (invertX)
            {
                normalizedPosition.x = 1.0f - normalizedPosition.x;
            }

            if (invertY)
            {
                normalizedPosition.y = 1.0f - normalizedPosition.y;
            }

            float width = maxX - minX;
            float height = maxY - minY;
            float handSpan = Mathf.Sqrt(width * width + height * height);
            normalizedScale = Mathf.InverseLerp(minHandSpan, maxHandSpan, handSpan);

            Vector3 wrist = landmarks[startIndex + WristIndex];
            Vector3 middleMcp = landmarks[startIndex + MiddleMcpIndex];
            Vector3 indexMcp = landmarks[startIndex + IndexMcpIndex];
            Vector3 pinkyMcp = landmarks[startIndex + PinkyMcpIndex];
            Vector2 palmAxis = new Vector2(pinkyMcp.x - indexMcp.x, pinkyMcp.y - indexMcp.y);
            if (palmAxis.sqrMagnitude < 0.0001f)
            {
                palmAxis = new Vector2(middleMcp.x - wrist.x, middleMcp.y - wrist.y);
            }

            float angleDegrees = Mathf.Atan2(palmAxis.y, palmAxis.x) * Mathf.Rad2Deg;
            normalizedRotation = Mathf.InverseLerp(minPalmAngleDegrees, maxPalmAngleDegrees, angleDegrees);
            return true;
        }

        private void ResolveDependencies()
        {
            if (targetController == null)
            {
                targetController = UnityEngine.Object.FindAnyObjectByType<ShadowMeshRootController>();
            }

            if (handReceiver == null)
            {
                handReceiver = UnityEngine.Object.FindAnyObjectByType<HandLandmarkUdpReceiver>();
            }
        }
    }
}
