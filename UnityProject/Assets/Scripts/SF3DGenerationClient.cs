using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public class SF3DGenerationClient : MonoBehaviour
    {
        private const int RequestTimeoutSeconds = 3600;
        private const string LabelerWarmupEndpoint = "/warmup-labeler";
        private const string TextureWarmupEndpoint = "/warmup-texture";

        [Header("Paths")]
        [SerializeField] private string baseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string classifyEndpoint = "/classify-silhouette";
        [SerializeField] private string textureEndpoint = "/generate-texture";
        [SerializeField] private string modelEndpoint = "/generate-3d";
        [SerializeField] private string outputDirectoryRelative = "../output/sf3d";
        [SerializeField] private string generatedGlbFileName = "shadow_model.glb";
        [SerializeField] private string texturePreviewFileName = "last_texture.png";
        [SerializeField] private string targetSceneAfterGeneration = "hologramOut";

        private Coroutine activeRoutine;
        private Coroutine activeClassificationRoutine;
        private Coroutine activeLabelerWarmupRoutine;
        private Coroutine activeTextureWarmupRoutine;

        public bool IsRunning => activeRoutine != null;
        public bool IsClassifying => activeClassificationRoutine != null;
        public bool IsLabelerWarmingUp => activeLabelerWarmupRoutine != null;
        public bool IsTextureWarmingUp => activeTextureWarmupRoutine != null;
        public bool HasSilhouetteLabel { get; private set; }
        public string BaseUrl => baseUrl;
        public string SilhouetteLabel { get; private set; } = string.Empty;
        public string SilhouetteVisualHint { get; private set; } = string.Empty;
        public string LastGenerationLabel { get; private set; } = string.Empty;
        public string LastInputPngPath { get; private set; } = string.Empty;
        public string LastTexturePath { get; private set; } = string.Empty;
        public string LastGeneratedGlbPath { get; private set; } = string.Empty;
        public event Action<string> TextureGenerated;
        public event Action<string> GlbGenerated;
        public event Action<string> SilhouetteClassified;

        [Serializable]
        private class ClassificationResponse
        {
            public string label = string.Empty;
            public string visual_hint = string.Empty;
        }

        public void ClassifySilhouette(string pngPath)
        {
            ResetSilhouetteLabel();

            if (string.IsNullOrWhiteSpace(pngPath))
            {
                Debug.LogWarning("SF3DGenerationClient: silhouette PNG path is empty.");
                return;
            }

            if (!File.Exists(pngPath))
            {
                Debug.LogWarning($"SF3DGenerationClient: silhouette PNG file was not found: {pngPath}");
                return;
            }

            if (activeClassificationRoutine != null)
            {
                Debug.LogWarning("SF3DGenerationClient: silhouette classification is already running.");
                return;
            }

            activeClassificationRoutine = StartCoroutine(ClassifySilhouetteCoroutine(pngPath));
        }

        public void ResetSilhouetteLabel()
        {
            HasSilhouetteLabel = false;
            SilhouetteLabel = string.Empty;
            SilhouetteVisualHint = string.Empty;
            LastGenerationLabel = string.Empty;
        }

        public void WarmupLabeler()
        {
            if (activeClassificationRoutine != null || HasSilhouetteLabel)
            {
                return;
            }

            if (activeLabelerWarmupRoutine != null)
            {
                return;
            }

            activeLabelerWarmupRoutine = StartCoroutine(WarmupLabelerCoroutine());
        }

        public void WarmupTexturePipeline()
        {
            if (activeTextureWarmupRoutine != null)
            {
                return;
            }

            activeTextureWarmupRoutine = StartCoroutine(WarmupTextureCoroutine());
        }

        public void GenerateFromPngBytes(byte[] pngBytes, string sourceFileName = "deformed_shadow.png")
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                Debug.LogWarning("SF3DGenerationClient: PNG data is empty.");
                return;
            }

            if (activeRoutine != null)
            {
                Debug.LogWarning("SF3DGenerationClient: generation is already running.");
                return;
            }

            if (!CanStartGeneration())
            {
                return;
            }

            string cleanSourceFileName = string.IsNullOrWhiteSpace(sourceFileName) ? "deformed_shadow.png" : sourceFileName;
            activeRoutine = StartCoroutine(GenerateFromPngBytesCoroutine(pngBytes, cleanSourceFileName, $"memory:{cleanSourceFileName}"));
        }

        private bool CanStartGeneration()
        {
            if (activeClassificationRoutine != null)
            {
                Debug.LogWarning("SF3DGenerationClient: generation is blocked until silhouette classification finishes.");
                return false;
            }

            if (!HasSilhouetteLabel)
            {
                Debug.LogWarning("SF3DGenerationClient: generation is blocked until silhouette classification is ready.");
                return false;
            }

            return true;
        }

        private IEnumerator WarmupLabelerCoroutine()
        {
            yield return WarmupCoroutine(BuildUrl(LabelerWarmupEndpoint), "Qwen labeler");
            activeLabelerWarmupRoutine = null;
        }

        private IEnumerator WarmupTextureCoroutine()
        {
            yield return WarmupCoroutine(BuildUrl(TextureWarmupEndpoint), "ControlNet texture pipeline");
            activeTextureWarmupRoutine = null;
        }

        private IEnumerator WarmupCoroutine(string url, string label)
        {
            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (HasRequestError(request))
            {
                Debug.LogWarning($"SF3DGenerationClient: {label} warmup failed: {GetRequestErrorMessage(request)}");
            }
            else
            {
                Debug.Log($"SF3DGenerationClient: {label} warmup complete.");
            }

        }

        private IEnumerator GenerateFromPngBytesCoroutine(byte[] pngBytes, string sourceFileName, string sourceDescription)
        {
            string generationLabel = SilhouetteLabel.Trim();
            string generationVisualHint = SilhouetteVisualHint.Trim();
            LastGenerationLabel = generationLabel;
            LastInputPngPath = sourceDescription;
            LastTexturePath = string.Empty;
            LastGeneratedGlbPath = string.Empty;

            Debug.Log($"SF3DGenerationClient: sending texture input '{sourceFileName}' from {sourceDescription} ({pngBytes.Length} bytes), label '{generationLabel}', visual_hint '{generationVisualHint}'.");
            UnityWebRequest textureRequest = CreateImagePostRequest(
                BuildUrl(textureEndpoint),
                pngBytes,
                sourceFileName,
                generationLabel,
                generationVisualHint);
            yield return textureRequest.SendWebRequest();

            if (HasRequestError(textureRequest))
            {
                Debug.LogWarning($"SF3DGenerationClient: texture generation failed: {GetRequestErrorMessage(textureRequest)}");
                textureRequest.Dispose();
                activeRoutine = null;
                yield break;
            }

            byte[] sf3dInputBytes = textureRequest.downloadHandler.data;
            LastTexturePath = SaveBytesToOutput(sf3dInputBytes, texturePreviewFileName);
            Debug.Log($"SF3DGenerationClient: saved texture preview: {LastTexturePath}");
            textureRequest.Dispose();
            TextureGenerated?.Invoke(LastTexturePath);

            UnityWebRequest modelRequest = CreateImagePostRequest(BuildUrl(modelEndpoint), sf3dInputBytes, "sf3d_input.png");
            yield return modelRequest.SendWebRequest();

            if (HasRequestError(modelRequest))
            {
                Debug.LogWarning($"SF3DGenerationClient: model generation failed: {GetRequestErrorMessage(modelRequest)}");
                modelRequest.Dispose();
                activeRoutine = null;
                yield break;
            }

            LastGeneratedGlbPath = SaveBytesToOutput(modelRequest.downloadHandler.data, generatedGlbFileName);
            Debug.Log($"SF3DGenerationClient: saved generated GLB: {LastGeneratedGlbPath}, label '{LastGenerationLabel}'.");

            modelRequest.Dispose();
            activeRoutine = null;

            HandleGlbGenerated(LastGeneratedGlbPath);
        }

        private void HandleGlbGenerated(string glbPath)
        {
            GlbGenerated?.Invoke(glbPath);
        }

        public void LoadTargetSceneAfterGeneration()
        {
            if (SceneManager.GetSceneByName(targetSceneAfterGeneration).isLoaded)
            {
                return;
            }

            SceneManager.LoadScene(targetSceneAfterGeneration, LoadSceneMode.Additive);
        }

        private IEnumerator ClassifySilhouetteCoroutine(string pngPath)
        {
            byte[] pngBytes;
            try
            {
                pngBytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"SF3DGenerationClient: silhouette PNG read failed for '{pngPath}': {exception.Message}");
                activeClassificationRoutine = null;
                yield break;
            }

            UnityWebRequest request = CreateImagePostRequest(BuildUrl(classifyEndpoint), pngBytes, Path.GetFileName(pngPath));
            yield return request.SendWebRequest();

            if (HasRequestError(request))
            {
                Debug.LogWarning($"SF3DGenerationClient: silhouette classification failed: {GetRequestErrorMessage(request)}");
                request.Dispose();
                activeClassificationRoutine = null;
                yield break;
            }

            string responseText = request.downloadHandler?.text;
            ClassificationResponse response = ParseClassificationResponse(responseText);
            if (response != null &&
                !string.IsNullOrWhiteSpace(response.label) &&
                !string.IsNullOrWhiteSpace(response.visual_hint))
            {
                SilhouetteLabel = response.label;
                SilhouetteVisualHint = response.visual_hint;
                HasSilhouetteLabel = true;
                SilhouetteClassified?.Invoke(SilhouetteLabel);
                Debug.Log($"SF3DGenerationClient: silhouette label is '{SilhouetteLabel}', visual_hint is '{SilhouetteVisualHint}'.");
            }

            request.Dispose();
            activeClassificationRoutine = null;
        }

        private UnityWebRequest CreateImagePostRequest(
            string url,
            byte[] imageBytes,
            string fileName,
            string label = null,
            string visualHint = null)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", imageBytes, fileName, "image/png");
            if (!string.IsNullOrWhiteSpace(label))
            {
                form.AddField("label", label);
            }
            if (!string.IsNullOrWhiteSpace(visualHint))
            {
                form.AddField("visual_hint", visualHint);
            }

            UnityWebRequest request = UnityWebRequest.Post(url, form);
            request.timeout = RequestTimeoutSeconds;
            return request;
        }

        private static ClassificationResponse ParseClassificationResponse(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<ClassificationResponse>(responseText);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"SF3DGenerationClient: silhouette classification response parse failed: {exception.Message}");
                return null;
            }
        }

        private string SaveBytesToOutput(byte[] bytes, string fileName)
        {
            string outputDirectory = GetOutputDirectoryAbsolute();
            Directory.CreateDirectory(outputDirectory);

            string path = Path.Combine(outputDirectory, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private string GetOutputDirectoryAbsolute()
        {
            if (Path.IsPathRooted(outputDirectoryRelative))
            {
                return Path.GetFullPath(outputDirectoryRelative);
            }

            string unityProjectDirectory = GetUnityProjectDirectoryAbsolute();
            return Path.GetFullPath(Path.Combine(unityProjectDirectory, outputDirectoryRelative));
        }

        private static string GetUnityProjectDirectoryAbsolute()
        {
            string dataPath = Path.GetFullPath(Application.dataPath);
            DirectoryInfo directory = Directory.GetParent(dataPath);

            while (directory != null)
            {
                bool hasUnityProjectLayout =
                    Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings"));

                if (hasUnityProjectLayout)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return Path.GetFullPath(Path.Combine(dataPath, ".."));
        }

        private string BuildUrl(string endpoint)
        {
            string cleanBase = baseUrl.TrimEnd('/');
            string cleanEndpoint = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.TrimStart('/');
            return $"{cleanBase}/{cleanEndpoint}";
        }

        private static bool HasRequestError(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.ConnectionError ||
                   request.result == UnityWebRequest.Result.ProtocolError ||
                   request.result == UnityWebRequest.Result.DataProcessingError;
        }

        private static string GetRequestErrorMessage(UnityWebRequest request)
        {
            string message = string.IsNullOrWhiteSpace(request.error) ? request.result.ToString() : request.error;
            string responseText = request.downloadHandler?.text;
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                message = $"{message}: {responseText}";
            }

            return message;
        }
    }
}
