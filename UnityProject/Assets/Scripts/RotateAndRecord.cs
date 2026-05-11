using UnityEngine;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Encoder;
using UnityEngine.Rendering;
using System.IO;
using System.Linq;

public class RotateAndRecord : MonoBehaviour
{
    public float recordTime = 15f;
    [SerializeField] private bool forceLightingOnStart = true;
    [SerializeField] private bool logSceneDiagnostics = true;
    [SerializeField] private float minimumDirectionalLightIntensity = 2f;
    [SerializeField] private Color fallbackAmbientLight = new Color(0.42f, 0.42f, 0.42f, 1f);

    private string inputDirectory = "C:/capstone/CAP2-Unity/sf3d_io/sf3d_outputs";
    private string outputDirectory = "C:/capstone_mp4";

    private Camera mainCam;
    private GlbLoader glbLoader;
    private float elapsed = 0f;
    private bool isRecording = false;
    private GameObject pivotTarget;

    private RecorderController recorderController;
    private RecorderControllerSettings controllerSettings;

    void Start()
    {
        if (forceLightingOnStart)
        {
            ApplyHologramLighting();
        }

        if (logSceneDiagnostics)
        {
            LogSceneDiagnostics("hologramOut Start before GLB load");
        }

        mainCam = Camera.main;
        if (mainCam == null)
            Debug.LogError("MainCamera 태그 확인");

        glbLoader = gameObject.AddComponent<GlbLoader>();
        glbLoader.inputDirectory = inputDirectory;
        glbLoader.OnLoadComplete += StartRecording;
        glbLoader.StartLoad();
    }

    void Update()
    {
        if (pivotTarget != null)
        {
            float circle = Time.time / recordTime;  // 진행률
            pivotTarget.transform.rotation = Quaternion.AngleAxis(circle * 360f, Vector3.up);
        }

        if (isRecording)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= recordTime)
            {
                StopRecording();
                elapsed = 0f;
            }
        }
    }

    void StartRecording()
    {
        if (forceLightingOnStart)
        {
            ApplyHologramLighting();
        }

        pivotTarget = CreateCenteredPivot(glbLoader.loadedModel);
        FitCameraToObject(mainCam, pivotTarget);

        if (logSceneDiagnostics)
        {
            LogSceneDiagnostics("hologramOut after GLB load");
        }

        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        int fileCount = Directory.GetFiles(outputDirectory, "*.mp4").Length;
        string fileName = Path.Combine(outputDirectory, (fileCount + 1).ToString());

        controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();

        movieSettings.name = "renderer";
        movieSettings.Enabled = true;
        movieSettings.OutputFile = fileName;
        movieSettings.OutputFormat =
            MovieRecorderSettings.VideoRecorderOutputFormat.MP4;

        movieSettings.EncoderSettings = new CoreEncoderSettings
        {
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            Codec = CoreEncoderSettings.OutputCodec.MP4
        };

        movieSettings.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = 1920,
            OutputHeight = 1080
        };

        controllerSettings.AddRecorderSettings(movieSettings);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 30;

        recorderController = new RecorderController(controllerSettings);
        recorderController.PrepareRecording();
        recorderController.StartRecording();

        isRecording = true;
        Debug.Log("녹화 시작: " + fileName);
    }

    void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;
        recorderController?.StopRecording();
        Debug.Log("녹화 완료");
    }

    void ApplyHologramLighting()
    {
        Light directional = FindObjectsOfType<Light>()
            .FirstOrDefault(light => light.type == LightType.Directional);

        if (directional != null)
        {
            directional.enabled = true;
            directional.intensity = Mathf.Max(directional.intensity, minimumDirectionalLightIntensity);
            RenderSettings.sun = directional;
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = fallbackAmbientLight;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = Mathf.Max(RenderSettings.reflectionIntensity, 1f);
    }

    void LogSceneDiagnostics(string label)
    {
        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string sunName = RenderSettings.sun != null ? RenderSettings.sun.name : "null";
        Debug.Log(
            $"[HologramDiag] {label} | activeScene={activeSceneName} | " +
            $"ambientMode={RenderSettings.ambientMode} | ambientLight={RenderSettings.ambientLight} | " +
            $"ambientIntensity={RenderSettings.ambientIntensity} | sun={sunName}");

        foreach (Light light in FindObjectsOfType<Light>())
        {
            Debug.Log(
                $"[HologramDiag] Light {light.name} | enabled={light.enabled} | " +
                $"type={light.type} | intensity={light.intensity} | color={light.color} | " +
                $"cullingMask={light.cullingMask}");
        }

        foreach (Camera camera in FindObjectsOfType<Camera>())
        {
            string target = camera.targetTexture != null
                ? $"{camera.targetTexture.width}x{camera.targetTexture.height}"
                : "screen";
            Debug.Log(
                $"[HologramDiag] Camera {camera.name} | enabled={camera.enabled} | " +
                $"tag={camera.tag} | depth={camera.depth} | clear={camera.clearFlags} | " +
                $"bg={camera.backgroundColor} | fov={camera.fieldOfView} | cullingMask={camera.cullingMask} | " +
                $"target={target}");
        }

        if (glbLoader != null && glbLoader.loadedModel != null)
        {
            Renderer[] renderers = glbLoader.loadedModel.GetComponentsInChildren<Renderer>();
            Debug.Log($"[HologramDiag] Loaded model={glbLoader.loadedModel.name} | rendererCount={renderers.Length}");
            foreach (Renderer renderer in renderers.Take(8))
            {
                Material material = renderer.sharedMaterial;
                string materialInfo = material == null
                    ? "material=null"
                    : $"material={material.name} shader={material.shader.name} color={GetMaterialColor(material)}";
                Debug.Log(
                    $"[HologramDiag] Renderer {renderer.name} | enabled={renderer.enabled} | " +
                    $"bounds={renderer.bounds} | {materialInfo}");
            }
        }

        GameObject[] dontDestroyRoots = GetDontDestroyOnLoadRoots();
        Debug.Log(
            $"[HologramDiag] DontDestroyOnLoad roots: " +
            $"{string.Join(", ", dontDestroyRoots.Select(root => root.name))}");
    }

    static Color GetMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }

        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }

        return Color.clear;
    }

    static GameObject[] GetDontDestroyOnLoadRoots()
    {
        GameObject probe = new GameObject("DontDestroyOnLoadProbe");
        DontDestroyOnLoad(probe);
        var roots = probe.scene.GetRootGameObjects()
            .Where(root => root != probe)
            .ToArray();
        Destroy(probe);
        return roots;
    }

    GameObject CreateCenteredPivot(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return model;

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        GameObject pivot = new GameObject("zeroPivot");
        pivot.transform.position = bounds.center;
        model.transform.SetParent(pivot.transform);
        model.transform.localPosition = model.transform.position - bounds.center;

        return pivot;
    }

    void FitCameraToObject(Camera cam, GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        float size = bounds.extents.magnitude;
        float distance = size * 2.5f;

        cam.transform.position = bounds.center + new Vector3(0, 0, -distance);
        cam.transform.LookAt(bounds.center);
    }

    void OnDestroy()
    {
        if (isRecording) StopRecording();
    }
}
