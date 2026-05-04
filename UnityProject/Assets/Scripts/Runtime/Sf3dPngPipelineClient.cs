using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ShadowPrototype
{
    public class Sf3dPngPipelineClient : MonoBehaviour
    {
        [Header("Server")]
        [SerializeField] private string baseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string textureEndpoint = "/generate-texture";
        [SerializeField] private string modelEndpoint = "/generate-3d";
        [SerializeField] private int requestTimeoutSeconds = 900;

        [Header("Pipeline")]
        [SerializeField] private bool runTextureGenerationFirst = true;
        [SerializeField] private bool preventConcurrentRequests = true;

        [Header("Output")]
        [SerializeField] private string outputDirectoryRelative = "sf3d_io/sf3d_outputs";
        [SerializeField] private string outputGlbPrefix = "shadow_asteroid";
        [SerializeField] private bool saveTexturePreview = true;
        [SerializeField] private string texturePreviewFileName = "last_texture.png";

        private Coroutine activeRoutine;

        public bool IsRunning => activeRoutine != null;
        public string LastInputPngPath { get; private set; } = string.Empty;
        public string LastTexturePath { get; private set; } = string.Empty;
        public string LastGeneratedGlbPath { get; private set; } = string.Empty;

        public void GenerateFromPng(string pngPath)
        {
            if (string.IsNullOrWhiteSpace(pngPath))
            {
                Debug.LogWarning("SF3D generation skipped because PNG path is empty.");
                return;
            }

            if (!File.Exists(pngPath))
            {
                Debug.LogWarning($"SF3D generation skipped because PNG does not exist: {pngPath}");
                return;
            }

            if (preventConcurrentRequests && activeRoutine != null)
            {
                Debug.LogWarning("SF3D generation is already running. Ignoring duplicate request.");
                return;
            }

            activeRoutine = StartCoroutine(GenerateFromPngCoroutine(pngPath));
        }

        private IEnumerator GenerateFromPngCoroutine(string pngPath)
        {
            LastInputPngPath = pngPath;
            LastTexturePath = string.Empty;
            LastGeneratedGlbPath = string.Empty;

            byte[] pngBytes;
            try
            {
                pngBytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"SF3D could not read PNG '{pngPath}': {exception.Message}");
                activeRoutine = null;
                yield break;
            }

            Debug.Log($"SF3D pipeline started from PNG: {pngPath}");

            byte[] sf3dInputBytes = pngBytes;
            if (runTextureGenerationFirst)
            {
                UnityWebRequest textureRequest = CreateImagePostRequest(BuildUrl(textureEndpoint), pngBytes, "deformed_shadow.png");
                yield return textureRequest.SendWebRequest();

                if (HasRequestError(textureRequest))
                {
                    Debug.LogWarning($"SF3D texture generation failed: {textureRequest.error}");
                    textureRequest.Dispose();
                    activeRoutine = null;
                    yield break;
                }

                sf3dInputBytes = textureRequest.downloadHandler.data;
                if (saveTexturePreview)
                {
                    LastTexturePath = SaveBytesToOutput(sf3dInputBytes, texturePreviewFileName);
                    Debug.Log($"SF3D texture preview saved: {LastTexturePath}");
                }

                textureRequest.Dispose();
            }

            UnityWebRequest modelRequest = CreateImagePostRequest(BuildUrl(modelEndpoint), sf3dInputBytes, "sf3d_input.png");
            yield return modelRequest.SendWebRequest();

            if (HasRequestError(modelRequest))
            {
                Debug.LogWarning($"SF3D model generation failed: {modelRequest.error}");
                modelRequest.Dispose();
                activeRoutine = null;
                yield break;
            }

            string glbFileName = $"{outputGlbPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.glb";
            LastGeneratedGlbPath = SaveBytesToOutput(modelRequest.downloadHandler.data, glbFileName);
            Debug.Log($"SF3D GLB saved: {LastGeneratedGlbPath}");

            modelRequest.Dispose();
            activeRoutine = null;
        }

        private UnityWebRequest CreateImagePostRequest(string url, byte[] imageBytes, string fileName)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", imageBytes, fileName, "image/png");

            UnityWebRequest request = UnityWebRequest.Post(url, form);
            request.timeout = Mathf.Max(requestTimeoutSeconds, 1);
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
            string cleanBase = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:8000" : baseUrl.TrimEnd('/');
            string cleanEndpoint = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.TrimStart('/');
            return $"{cleanBase}/{cleanEndpoint}";
        }

        private static bool HasRequestError(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.ConnectionError ||
                   request.result == UnityWebRequest.Result.ProtocolError ||
                   request.result == UnityWebRequest.Result.DataProcessingError;
        }
    }
}
