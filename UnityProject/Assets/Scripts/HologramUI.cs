using UnityEngine;
using UnityEngine.UI;

public class HologramUI : MonoBehaviour
{
    private HologramCamera cam;

    private Canvas canvas;
    private RawImage imgFront;
    private RawImage imgLeft;
    private RawImage imgRight;
    public float imageSizeRatio = 0.6f;  // 각 ui 크기 비율

    void Start()
    {
        cam = GetComponent<HologramCamera>();
        if (cam == null)
        {
            Debug.LogError("Tag HologramCamera");
            return;
        }

        SetupCanvas();
    }

    void SetupCanvas()
    {
        // Canvas 생성
        GameObject canvasGo = new GameObject("HologramCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // 3개 면 생성 및 배치
        float screenRef = Mathf.Min(Screen.width, Screen.height);
        float finalSize = screenRef * imageSizeRatio;

        // --- Front 배치 ---
        imgFront = CreateView("Front", canvas.transform);
        SetupTransform(imgFront, new Vector2(0.5f, 0.75f), finalSize, 180f);

        // --- Left 배치 ---
        imgLeft = CreateView("Left", canvas.transform);
        SetupTransform(imgLeft, new Vector2(0.25f, 0.3f), finalSize, -90f);

        // --- Right 배치 ---
        imgRight = CreateView("Right", canvas.transform);
        SetupTransform(imgRight, new Vector2(0.75f, 0.3f), finalSize, 90f);
    }

    RawImage CreateView(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RawImage>();
    }

    void SetupTransform(RawImage img, Vector2 pos, float size, float zRot)
    {
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = pos;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;
        rt.localEulerAngles = new Vector3(0, 0, zRot);
    }

    void Update()
    {
        if (cam == null || imgFront == null) return;

        // 카메라 영상 연결
        if (imgFront.texture == null && cam.rtFront != null) imgFront.texture = cam.rtFront;
        if (imgLeft.texture == null && cam.rtLeft != null) imgLeft.texture = cam.rtLeft;
        if (imgRight.texture == null && cam.rtRight != null) imgRight.texture = cam.rtRight;
    }
}