using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ShadowPrototype
{
    public class QwenClient : MonoBehaviour
    {
        private const int RequestTimeoutSeconds = 3600;
        private const string LabelerWarmupEndpoint = "/warmup-labeler";

        [SerializeField] private string baseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string classifyEndpoint = "/classify-silhouette";

        private Coroutine activeClassificationRoutine;
        private Coroutine activeLabelerWarmupRoutine;

        public bool IsClassifying => activeClassificationRoutine != null;
        public bool IsLabelerWarmingUp => activeLabelerWarmupRoutine != null;
        public bool IsLabelerReady { get; private set; }
        public bool HasKeyword { get; private set; }
        public string BaseUrl => baseUrl;
        public string Keyword { get; private set; } = string.Empty;
        public event Action<string> KeywordClassified;

        [Serializable]
        private class ClassificationResponse
        {
            public string label = string.Empty;
        }

        public void WarmupLabeler()
        {
            if (activeLabelerWarmupRoutine != null || IsLabelerReady)
            {
                return;
            }

            activeLabelerWarmupRoutine = StartCoroutine(WarmupLabelerCoroutine());
        }

        public void ClassifySilhouette(string pngPath)
        {
            ResetKeyword();

            if (string.IsNullOrWhiteSpace(pngPath))
            {
                Debug.LogWarning("QwenClient: silhouette PNG path is empty.");
                return;
            }

            if (!File.Exists(pngPath))
            {
                Debug.LogWarning($"QwenClient: silhouette PNG file was not found: {pngPath}");
                return;
            }

            if (activeClassificationRoutine != null)
            {
                Debug.LogWarning("QwenClient: silhouette classification is already running.");
                return;
            }

            activeClassificationRoutine = StartCoroutine(ClassifySilhouetteCoroutine(pngPath));
        }

        public void ResetKeyword()
        {
            HasKeyword = false;
            Keyword = string.Empty;
        }

        private IEnumerator WarmupLabelerCoroutine()
        {
            IsLabelerReady = false;
            using UnityWebRequest request = new UnityWebRequest(BuildUrl(LabelerWarmupEndpoint), UnityWebRequest.kHttpVerbPOST);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            yield return request.SendWebRequest();

            IsLabelerReady = !HasRequestError(request);
            if (IsLabelerReady)
            {
                Debug.Log("QwenClient: Qwen labeler warmup complete.");
            }
            else
            {
                Debug.LogWarning($"QwenClient: Qwen labeler warmup failed: {GetRequestErrorMessage(request)}");
            }

            activeLabelerWarmupRoutine = null;
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
                Debug.LogWarning($"QwenClient: silhouette PNG read failed for '{pngPath}': {exception.Message}");
                activeClassificationRoutine = null;
                yield break;
            }

            UnityWebRequest request = CreateImagePostRequest(BuildUrl(classifyEndpoint), pngBytes, Path.GetFileName(pngPath));
            yield return request.SendWebRequest();

            if (HasRequestError(request))
            {
                Debug.LogWarning($"QwenClient: silhouette classification failed: {GetRequestErrorMessage(request)}");
                request.Dispose();
                activeClassificationRoutine = null;
                yield break;
            }

            string responseText = request.downloadHandler?.text;
            ClassificationResponse response = ParseClassificationResponse(responseText);
            if (response != null && !string.IsNullOrWhiteSpace(response.label))
            {
                Keyword = response.label.Trim();
                HasKeyword = true;
                IsLabelerReady = false;
                KeywordClassified?.Invoke(Keyword);
                Debug.Log($"QwenClient: keyword is '{Keyword}'.");
            }

            request.Dispose();
            activeClassificationRoutine = null;
        }

        private UnityWebRequest CreateImagePostRequest(string url, byte[] imageBytes, string fileName)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", imageBytes, fileName, "image/png");
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
                Debug.LogWarning($"QwenClient: silhouette classification response parse failed: {exception.Message}");
                return null;
            }
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
