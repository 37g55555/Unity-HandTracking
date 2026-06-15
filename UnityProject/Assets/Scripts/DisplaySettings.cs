using UnityEngine;

namespace ShadowPrototype
{
    internal static class DisplayRoutingSettings
    {
        public const int HologramUnityDisplayIndex = 1;
        public const int TerminalMonitorPositionIndex = 1;
    }

    internal static class HologramPanelLayout
    {
        public const float ReferenceDisplayWidth = 1920f;
        public const float ReferenceDisplayHeight = 1080f;
        public const float ReferencePanelSizePixels = 540f;

        public static readonly Vector2 FrontAnchor = new Vector2(0.5f, 0.75f);
        public static readonly Vector2 LeftAnchor = new Vector2(0.25f, 0.25f);
        public static readonly Vector2 RightAnchor = new Vector2(0.75f, 0.25f);
        public static readonly Vector2 FrontOffset = Vector2.zero;
        public static readonly Vector2 LeftOffset = new Vector2(-130f, 0f);
        public static readonly Vector2 RightOffset = new Vector2(130f, 0f);

        public static float CalculatePanelSize(Vector2Int displaySize)
        {
            float displayWidth = Mathf.Max(1f, displaySize.x);
            float displayHeight = Mathf.Max(1f, displaySize.y);
            float referenceScale = Mathf.Min(
                displayWidth / ReferenceDisplayWidth,
                displayHeight / ReferenceDisplayHeight);
            return Mathf.Max(1f, ReferencePanelSizePixels * referenceScale);
        }

        public static Vector2Int GetDisplaySize(int targetDisplay)
        {
            if (targetDisplay >= 0 && targetDisplay < Display.displays.Length)
            {
                Display display = Display.displays[targetDisplay];
                if (display.renderingWidth > 0 && display.renderingHeight > 0)
                {
                    return new Vector2Int(display.renderingWidth, display.renderingHeight);
                }
            }

            return new Vector2Int(Screen.width, Screen.height);
        }
    }
}
