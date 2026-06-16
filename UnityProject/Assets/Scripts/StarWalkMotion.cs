using UnityEngine;

namespace ShadowPrototype
{
    public static class StarWalkMotion
    {
        private const float MinimumHorizontalDistance = 0.08f;
        private const float StepLength = 0.85f;
        private const float InPlaceStepsPerSecond = 2.25f;
        private const float BobHeight = 0.08f;
        private const float TiltDegrees = 6.0f;
        private const float MovingStrideFloor = 0.18f;

        public static void ApplyWorld(
            Transform target,
            Vector3 basePosition,
            Vector3 startPosition,
            Vector3 targetPosition,
            float normalizedProgress,
            Quaternion restLocalRotation)
        {
            if (target == null)
            {
                return;
            }

            target.position = BuildWalkPosition(basePosition, startPosition, targetPosition, normalizedProgress);
            target.localRotation = BuildWalkRotation(startPosition, targetPosition, normalizedProgress, restLocalRotation);
        }

        public static void ApplyLocal(
            Transform target,
            Vector3 baseLocalPosition,
            Vector3 startLocalPosition,
            Vector3 targetLocalPosition,
            float normalizedProgress,
            Quaternion restLocalRotation)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = BuildWalkPosition(baseLocalPosition, startLocalPosition, targetLocalPosition, normalizedProgress);
            target.localRotation = BuildWalkRotation(startLocalPosition, targetLocalPosition, normalizedProgress, restLocalRotation);
        }

        public static void FinishWorld(Transform target, Vector3 targetPosition, Quaternion restLocalRotation)
        {
            if (target == null)
            {
                return;
            }

            target.position = targetPosition;
            target.localRotation = restLocalRotation;
        }

        public static void ApplyWorldInPlace(
            Transform target,
            Vector3 basePosition,
            float elapsedSeconds,
            float direction,
            Quaternion restLocalRotation)
        {
            if (target == null)
            {
                return;
            }

            float phase = Mathf.Max(0.0f, elapsedSeconds) * InPlaceStepsPerSecond * Mathf.PI * 2.0f;
            float walkDirection = Mathf.Approximately(direction, 0.0f) ? 1.0f : Mathf.Sign(direction);
            target.position = basePosition + (Vector3.up * BuildStrideBob(phase, 1.0f, MovingStrideFloor));
            target.localRotation = BuildStrideRotation(phase, 1.0f, walkDirection, restLocalRotation);
        }

        public static void FinishLocal(Transform target, Vector3 targetLocalPosition, Quaternion restLocalRotation)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = targetLocalPosition;
            target.localRotation = restLocalRotation;
        }

        private static Vector3 BuildWalkPosition(
            Vector3 basePosition,
            Vector3 startPosition,
            Vector3 targetPosition,
            float normalizedProgress)
        {
            Vector3 delta = targetPosition - startPosition;
            if (Mathf.Abs(delta.x) < MinimumHorizontalDistance)
            {
                return basePosition;
            }

            float progress = Mathf.Clamp01(normalizedProgress);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            float distance = Mathf.Abs(delta.x);
            float stepCount = Mathf.Max(1.0f, distance / StepLength);
            float phase = progress * stepCount * Mathf.PI * 2.0f;
            return basePosition + (Vector3.up * BuildStrideBob(phase, envelope, MovingStrideFloor));
        }

        private static Quaternion BuildWalkRotation(
            Vector3 startPosition,
            Vector3 targetPosition,
            float normalizedProgress,
            Quaternion restLocalRotation)
        {
            Vector3 delta = targetPosition - startPosition;
            if (Mathf.Abs(delta.x) < MinimumHorizontalDistance)
            {
                return restLocalRotation;
            }

            float progress = Mathf.Clamp01(normalizedProgress);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            float distance = Mathf.Abs(delta.x);
            float stepCount = Mathf.Max(1.0f, distance / StepLength);
            float phase = progress * stepCount * Mathf.PI * 2.0f;
            float direction = Mathf.Sign(delta.x);
            return BuildStrideRotation(phase, envelope, direction, restLocalRotation);
        }

        private static float BuildStrideBob(float phase, float envelope, float strideFloor)
        {
            float lift = Mathf.Lerp(Mathf.Clamp01(strideFloor), 1.0f, Mathf.Abs(Mathf.Sin(phase)));
            return lift * BobHeight * Mathf.Clamp01(envelope);
        }

        private static Quaternion BuildStrideRotation(
            float phase,
            float envelope,
            float direction,
            Quaternion restLocalRotation)
        {
            float tilt = -Mathf.Cos(phase) * TiltDegrees * Mathf.Clamp01(envelope) * direction;
            return restLocalRotation * Quaternion.Euler(0.0f, 0.0f, tilt);
        }
    }
}
