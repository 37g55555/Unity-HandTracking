using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ShadowPrototype
{
    public class SF3DGenerationClient : MonoBehaviour
    {
        private const int RequestTimeoutSeconds = 3600;

        [Header("Paths")]
        [SerializeField] private string baseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string textureEndpoint = "/generate-texture";
        [SerializeField] private string modelEndpoint = "/generate-3d";
        [SerializeField] private string outputDirectoryRelative = "../output/sf3d";
        [SerializeField] private string generatedGlbFilePrefix = "shadow_asteroid";
        [SerializeField] private string texturePreviewFileName = "last_texture.png";
        [SerializeField] private string targetSceneAfterGeneration = "hologramOut";

        private Coroutine activeRoutine;

        public bool IsRunning => activeRoutine != null;
        public string BaseUrl => baseUrl;
        public string LastInputPngPath { get; private set; } = string.Empty;
        public string LastTexturePath { get; private set; } = string.Empty;
        public string LastGeneratedGlbPath { get; private set; } = string.Empty;
        public event Action<string> GlbGenerated;

        public void GenerateFromPng(string pngPath)
        {
            if (string.IsNullOrWhiteSpace(pngPath))
            {
                Debug.LogWarning("SF3DGenerationClient: PNG path is empty.");
                return;
            }

            if (!File.Exists(pngPath))
            {
                Debug.LogWarning($"SF3DGenerationClient: PNG file was not found: {pngPath}");
                return;
            }

            if (activeRoutine != null)
            {
                Debug.LogWarning("SF3DGenerationClient: generation is already running.");
                return;
            }

            activeRoutine = StartCoroutine(GenerateFromPngCoroutine(pngPath));
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

            string cleanSourceFileName = string.IsNullOrWhiteSpace(sourceFileName) ? "deformed_shadow.png" : sourceFileName;
            activeRoutine = StartCoroutine(GenerateFromPngBytesCoroutine(pngBytes, cleanSourceFileName, $"memory:{cleanSourceFileName}"));
        }

        private IEnumerator GenerateFromPngCoroutine(string pngPath)
        {
            byte[] pngBytes;
            try
            {
                pngBytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"SF3DGenerationClient: PNG read failed for '{pngPath}': {exception.Message}");
                activeRoutine = null;
                yield break;
            }

            yield return GenerateFromPngBytesCoroutine(pngBytes, Path.GetFileName(pngPath), pngPath);
        }

        private IEnumerator GenerateFromPngBytesCoroutine(byte[] pngBytes, string sourceFileName, string sourceDescription)
        {
            LastInputPngPath = sourceDescription;
            LastTexturePath = string.Empty;
            LastGeneratedGlbPath = string.Empty;

            byte[] sf3dInputBytes = pngBytes;
            UnityWebRequest textureRequest = CreateImagePostRequest(BuildUrl(textureEndpoint), pngBytes, sourceFileName);
            yield return textureRequest.SendWebRequest();

            if (HasRequestError(textureRequest))
            {
                Debug.LogWarning($"SF3DGenerationClient: texture generation failed: {GetRequestErrorMessage(textureRequest)}");
                textureRequest.Dispose();
                activeRoutine = null;
                yield break;
            }

            sf3dInputBytes = textureRequest.downloadHandler.data;
            LastTexturePath = SaveBytesToOutput(sf3dInputBytes, texturePreviewFileName);
            textureRequest.Dispose();

            UnityWebRequest modelRequest = CreateImagePostRequest(BuildUrl(modelEndpoint), sf3dInputBytes, "sf3d_input.png");
            yield return modelRequest.SendWebRequest();

            if (HasRequestError(modelRequest))
            {
                Debug.LogWarning($"SF3DGenerationClient: model generation failed: {GetRequestErrorMessage(modelRequest)}");
                modelRequest.Dispose();
                activeRoutine = null;
                yield break;
            }

            string glbFileName = $"{generatedGlbFilePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.glb";
            LastGeneratedGlbPath = SaveBytesToOutput(modelRequest.downloadHandler.data, glbFileName);

            modelRequest.Dispose();
            activeRoutine = null;

            HandleGlbGenerated(LastGeneratedGlbPath);
        }

        private void HandleGlbGenerated(string glbPath)
        {
            GlbGenerated?.Invoke(glbPath);

            UnityEngine.SceneManagement.SceneManager.LoadScene(targetSceneAfterGeneration);
        }

        private UnityWebRequest CreateImagePostRequest(string url, byte[] imageBytes, string fileName)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", imageBytes, fileName, "image/png");

            UnityWebRequest request = UnityWebRequest.Post(url, form);
            request.timeout = RequestTimeoutSeconds;
            return request;
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
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, outputDirectoryRelative));
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
