using UnityEngine;
using UnityEngine.InputSystem;

namespace ShadowPrototype
{
    public class MockScaleInput : MonoBehaviour
    {
        [SerializeField] private bool enableInput;
        [SerializeField] private ShadowMeshRootController targetController;
        [SerializeField] private float mouseWheelSensitivity = 0.1f;
        [SerializeField] private float keyboardStep = 0.05f;

        public void Configure(ShadowMeshRootController controller)
        {
            targetController = controller;
        }

        private void Awake()
        {
            ResolveController();
        }

        private void Update()
        {
            if (!enableInput)
            {
                return;
            }

            if (targetController == null)
            {
                ResolveController();
                if (targetController == null)
                {
                    return;
                }
            }

            float nextValue = targetController.CurrentNormalizedScale;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                nextValue += mouse.scroll.ReadValue().y * mouseWheelSensitivity;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                targetController.SetScaleNormalized(nextValue);
                return;
            }

            if (keyboard.equalsKey.isPressed || keyboard.rightBracketKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                nextValue += keyboardStep;
            }

            if (keyboard.minusKey.isPressed || keyboard.leftBracketKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                nextValue -= keyboardStep;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                nextValue = 0.0f;
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                nextValue = 0.5f;
            }
            else if (keyboard.digit3Key.wasPressedThisFrame)
            {
                nextValue = 1.0f;
            }

            targetController.SetScaleNormalized(nextValue);
        }

        private void ResolveController()
        {
            if (targetController == null)
            {
                targetController = UnityEngine.Object.FindAnyObjectByType<ShadowMeshRootController>();
            }
        }
    }
}
