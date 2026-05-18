using UnityEngine;
using UnityEngine.UI;

public class HologramDisplayManager : MonoBehaviour
{
    private const float CameraDistance = 3f;
    private const float CameraHeight = 0.5f;
    private const float CameraFov = 40f;
    private const float FrontAngle = 0f;
    private const float LeftAngle = 120f;
    private const float RightAngle = 240f;
    private const float ImageSizeRatio = 0.65f;

    private RenderTexture frontTexture;
    private RenderTexture leftTexture;
    private RenderTexture rightTexture;
    private Canvas canvas;

    private void Awake()
    {
        int resolution = Mathf.Min(Screen.width, Screen.height);

        frontTexture = new RenderTexture(resolution, resolution, 16);
        leftTexture = new RenderTexture(resolution, resolution, 16);
        rightTexture = new RenderTexture(resolution, resolution, 16);

        CreateCamera("Cam_Front", FrontAngle, frontTexture);
        CreateCamera("Cam_Left", LeftAngle, leftTexture);
        CreateCamera("Cam_Right", RightAngle, rightTexture);
    }

    private void Start()
    {
        SetupDisplayCanvas();
    }

    private void CreateCamera(string cameraName, float yAngle, RenderTexture targetTexture)
    {
        GameObject cameraObject = new GameObject(cameraName);
        cameraObject.transform.SetParent(transform);

        float radians = yAngle * Mathf.Deg2Rad;
        cameraObject.transform.localPosition = new Vector3(
            Mathf.Sin(radians) * CameraDistance,
            CameraHeight,
            Mathf.Cos(radians) * CameraDistance);
        cameraObject.transform.LookAt(transform.position);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = CameraFov;
        camera.targetTexture = targetTexture;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
    }

    private void SetupDisplayCanvas()
    {
        GameObject canvasObject = new GameObject("HologramCanvas");
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        float screenReference = Mathf.Min(Screen.width, Screen.height);
        float imageSize = screenReference * ImageSizeRatio;

        RawImage frontImage = CreateView("Front", canvas.transform);
        SetupTransform(frontImage, new Vector2(0.5f, 0.75f), imageSize, 180f);
        frontImage.texture = frontTexture;

        RawImage leftImage = CreateView("Left", canvas.transform);
        SetupTransform(leftImage, new Vector2(0.25f, 0.3f), imageSize, -90f);
        leftImage.texture = leftTexture;

        RawImage rightImage = CreateView("Right", canvas.transform);
        SetupTransform(rightImage, new Vector2(0.75f, 0.3f), imageSize, 90f);
        rightImage.texture = rightTexture;
    }

    private static RawImage CreateView(string viewName, Transform parent)
    {
        GameObject viewObject = new GameObject(viewName);
        viewObject.transform.SetParent(parent, false);
        return viewObject.AddComponent<RawImage>();
    }

    private static void SetupTransform(RawImage image, Vector2 anchor, float size, float zRotation)
    {
        RectTransform rectTransform = image.rectTransform;
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(size, size);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localEulerAngles = new Vector3(0f, 0f, zRotation);
    }
}
