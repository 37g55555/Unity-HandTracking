using UnityEngine;

public class HologramCamera : MonoBehaviour
{    
    private int renderWidth = 720;
    private int renderHeight = 720;

    public float cameraDistance = 3f;  // 카메라 물체 직선거리 = 줌
    public float cameraHeight = 0.5f;
    public float cameraFOV = 40f;  // 시야각 , 30~45 홀로그램 특화 

    private float angleFront = 0f;
    private float angleLeft  = 120f;
    private float angleRight = 240f;

    [HideInInspector] public Camera camFront;
    [HideInInspector] public Camera camLeft;
    [HideInInspector] public Camera camRight;

    // 카메라 출력 ui화 //
    [HideInInspector] public RenderTexture rtFront;
    [HideInInspector] public RenderTexture rtLeft;
    [HideInInspector] public RenderTexture rtRight;

    void Awake()
    {
        int screenW = Screen.width;
        int screenH = Screen.height;

        int res = Mathf.Min(Screen.width, Screen.height);

        rtFront = new RenderTexture(res, res, 16);
        rtLeft = new RenderTexture(res, res, 16);
        rtRight = new RenderTexture(res, res, 16);

        camFront = CreateCamera("Cam_Front", angleFront, rtFront);
        camLeft  = CreateCamera("Cam_Left",  angleLeft,  rtLeft);
        camRight = CreateCamera("Cam_Right", angleRight, rtRight);
    }

    Camera CreateCamera(string camName, float yAngle, RenderTexture rt)
    {
        GameObject go = new GameObject(camName);
        go.transform.SetParent(transform);

        float rad = yAngle * Mathf.Deg2Rad;

        go.transform.localPosition = new Vector3(
            Mathf.Sin(rad) * cameraDistance,
            cameraHeight,
            Mathf.Cos(rad) * cameraDistance
        );
        go.transform.LookAt(transform.position);

        Camera cam = go.AddComponent<Camera>();

        cam.fieldOfView    = cameraFOV;
        cam.targetTexture  = rt;
        cam.clearFlags     = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        return cam;
    }
}