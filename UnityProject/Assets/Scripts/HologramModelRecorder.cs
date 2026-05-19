using System.IO;
using GLTFast;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.Rendering;

public class HologramModelRecorder : MonoBehaviour
{
    private const float RecordingDurationSeconds = 15f;
    private const bool ForceLightingOnStart = true;
    private const float MinimumDirectionalLightIntensity = 2f;
    private static readonly Color FallbackAmbientLight = new Color(0.42f, 0.42f, 0.42f, 1f);

    [Header("Paths")]
    [SerializeField] private string inputDirectory = "D:/Unity-HandTracking/output/sf3d";
    [SerializeField] private string outputDirectory = "D:/Unity-HandTracking/output/recordings";

    private Camera mainCamera;
    private GameObject loadedModel;
    private float elapsedSeconds;
    private bool isRecording;
    private GameObject pivotTarget;
    private RecorderController recorderController;
    private RecorderControllerSettings controllerSettings;

    private void Start()
    {
        if (ForceLightingOnStart)
        {
            ApplyHologramLighting();
        }

        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("HologramModelRecorder: MainCamera tag is missing.");
        }

        LoadLatestModel();
    }

    private void Update()
    {
        if (pivotTarget != null)
        {
            float rotationProgress = Time.time / RecordingDurationSeconds;
            pivotTarget.transform.rotation = Quaternion.AngleAxis(rotationProgress * 360f, Vector3.up);
        }

        if (!isRecording)
        {
            return;
        }

        elapsedSeconds += Time.deltaTime;
        if (elapsedSeconds >= RecordingDurationSeconds)
        {
            StopRecording();
            elapsedSeconds = 0f;
        }
    }

    private void StartRecording()
    {
        if (ForceLightingOnStart)
        {
            ApplyHologramLighting();
        }

        pivotTarget = CreateCenteredPivot(loadedModel);
        FitCameraToObject(mainCamera, pivotTarget);

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        int fileCount = Directory.GetFiles(outputDirectory, "*.mp4").Length;
        string fileName = Path.Combine(outputDirectory, (fileCount + 1).ToString());

        controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();

        movieSettings.name = "renderer";
        movieSettings.Enabled = true;
        movieSettings.OutputFile = fileName;
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
    }

    private void StopRecording()
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        recorderController?.StopRecording();
    }

    private async void LoadLatestModel()
    {
        string latestFile = GetLatestGlbFile(inputDirectory);
        if (latestFile == null)
        {
            Debug.LogError("HologramModelRecorder: no .glb file found in folder: " + inputDirectory);
            return;
        }

        await LoadGlb(latestFile);
    }

    private static string GetLatestGlbFile(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Debug.LogError("HologramModelRecorder: folder not found: " + folderPath);
            return null;
        }

        string latestFile = null;
        foreach (string file in Directory.GetFiles(folderPath, "*.glb"))
        {
            if (latestFile == null || File.GetLastWriteTime(file) > File.GetLastWriteTime(latestFile))
            {
                latestFile = file;
            }
        }

        return latestFile;
    }

    private async System.Threading.Tasks.Task LoadGlb(string path)
    {
        if (loadedModel != null)
        {
            Destroy(loadedModel);
        }

        var gltf = new GltfImport();
        bool success = await gltf.Load("file://" + path);
        if (!success)
        {
            Debug.LogError("HologramModelRecorder: GLB load failed: " + path);
            return;
        }

        loadedModel = new GameObject("HologramObject");
        await gltf.InstantiateMainSceneAsync(loadedModel.transform);
        CenterAndNormalize(loadedModel);
        StartRecording();
    }

    private static void ApplyHologramLighting()
    {
        Light directional = null;
        foreach (Light light in FindObjectsOfType<Light>())
        {
            if (light.type == LightType.Directional)
            {
                directional = light;
                break;
            }
        }

        if (directional != null)
        {
            directional.enabled = true;
            directional.intensity = Mathf.Max(directional.intensity, MinimumDirectionalLightIntensity);
            RenderSettings.sun = directional;
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = FallbackAmbientLight;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = Mathf.Max(RenderSettings.reflectionIntensity, 1f);
    }

    private static GameObject CreateCenteredPivot(GameObject model)
    {
        if (!TryGetRendererBounds(model, out Bounds bounds))
        {
            return model;
        }

        GameObject pivot = new GameObject("zeroPivot");
        pivot.transform.position = bounds.center;
        model.transform.SetParent(pivot.transform);
        model.transform.localPosition = model.transform.position - bounds.center;

        return pivot;
    }

    private static void FitCameraToObject(Camera camera, GameObject target)
    {
        if (camera == null || target == null)
        {
            return;
        }

        if (!TryGetRendererBounds(target, out Bounds bounds))
        {
            return;
        }

        float size = bounds.extents.magnitude;
        float distance = size * 2.5f;

        camera.transform.position = bounds.center + new Vector3(0f, 0f, -distance);
        camera.transform.LookAt(bounds.center);
    }

    private static void CenterAndNormalize(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        model.transform.position = -bounds.center;

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0)
        {
            model.transform.localScale = Vector3.one * (1f / maxSize);
        }
    }

    private static bool TryGetRendererBounds(GameObject target, out Bounds bounds)
    {
        bounds = default;
        if (target == null)
        {
            return false;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return false;
        }

        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        return true;
    }

    private void OnDestroy()
    {
        if (isRecording)
        {
            StopRecording();
        }
    }
}
