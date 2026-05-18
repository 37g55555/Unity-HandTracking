using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ShadowPrototype
{
    public class ShadowMeshFileLoader : MonoBehaviour
    {
        private const float InitialLoadDelaySeconds = 0.5f;
        private const float RetryDelaySeconds = 0.25f;
        private const int MaxLoadAttempts = 3;
        private const float PollingIntervalSeconds = 1.0f;

        [Header("Paths")]
        [SerializeField] private string relativeWatchDirectory = @"..\output\shadowmesh";
        [SerializeField] private string meshFileName = "shadow_mesh.obj";
        [SerializeField] private string metadataFileName = "shadow_metadata.json";
        [SerializeField] private string absoluteWatchDirectoryOverride = @"D:\Unity-HandTracking\output\shadowmesh";

        [SerializeField] private ShadowMeshDeformer shadowMeshDeformer;
        [SerializeField] private GameStateManager stateManager;

        private FileSystemWatcher watcher;
        private readonly object pendingLock = new object();
        private string pendingMeshPath;
        private Coroutine activeLoadRoutine;
        private DateTime? minimumAcceptedMeshWriteTimeUtc;
        private DateTime lastPolledMeshWriteTimeUtc = DateTime.MinValue;
        private float nextPollTime;
        private bool loadExistingMeshOnStart;

        public string WatchDirectoryAbsolute => GetWatchDirectoryAbsolute();

        public void SetLoadExistingMeshOnStart(bool shouldLoadExistingMeshOnStart)
        {
            loadExistingMeshOnStart = shouldLoadExistingMeshOnStart;

            if (!loadExistingMeshOnStart && !minimumAcceptedMeshWriteTimeUtc.HasValue)
            {
                minimumAcceptedMeshWriteTimeUtc = DateTime.UtcNow;
            }
        }

        public void SetMinimumAcceptedMeshWriteTimeUtc(DateTime utcTimestamp)
        {
            minimumAcceptedMeshWriteTimeUtc = utcTimestamp;
        }

        public void ClearMinimumAcceptedMeshWriteTimeUtc()
        {
            minimumAcceptedMeshWriteTimeUtc = null;
        }

        public void LoadExistingMesh()
        {
            string existingMeshPath = Path.Combine(GetWatchDirectoryAbsolute(), meshFileName);
            ClearMinimumAcceptedMeshWriteTimeUtc();

            if (!File.Exists(existingMeshPath))
            {
                stateManager?.OnShadowMeshLoadFailed(existingMeshPath);
                return;
            }

            lastPolledMeshWriteTimeUtc = File.GetLastWriteTimeUtc(existingMeshPath);
            QueueMeshLoad(existingMeshPath);
        }

        private void Start()
        {
            SetupWatcher();

            string existingMeshPath = Path.Combine(GetWatchDirectoryAbsolute(), meshFileName);
            if (loadExistingMeshOnStart && File.Exists(existingMeshPath))
            {
                QueueMeshLoad(existingMeshPath);
            }
            else if (!loadExistingMeshOnStart)
            {
                IgnoreExistingMeshUntilNextWrite(existingMeshPath);
            }
        }

        private void Update()
        {
            PollMeshFileIfNeeded();

            if (activeLoadRoutine != null)
            {
                return;
            }

            string pathToLoad = null;
            lock (pendingLock)
            {
                if (!string.IsNullOrEmpty(pendingMeshPath))
                {
                    pathToLoad = pendingMeshPath;
                    pendingMeshPath = null;
                }
            }

            if (!string.IsNullOrEmpty(pathToLoad))
            {
                activeLoadRoutine = StartCoroutine(LoadMeshCoroutine(pathToLoad));
            }
        }

        private void OnDisable()
        {
            DisposeWatcher();
        }

        private void OnDestroy()
        {
            DisposeWatcher();
        }

        private void SetupWatcher()
        {
            string watchDirectory = GetWatchDirectoryAbsolute();
            if (!Directory.Exists(watchDirectory))
            {
                Debug.LogWarning($"ShadowMeshFileLoader: watch directory was not found: {watchDirectory}");
                return;
            }

            watcher = new FileSystemWatcher(watchDirectory, meshFileName);
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
            watcher.Changed += OnMeshFileEvent;
            watcher.Created += OnMeshFileEvent;
            watcher.Renamed += OnMeshFileRenamed;
            watcher.EnableRaisingEvents = true;
        }

        private void OnMeshFileEvent(object sender, FileSystemEventArgs eventArgs)
        {
            QueueMeshLoad(eventArgs.FullPath);
        }

        private void OnMeshFileRenamed(object sender, RenamedEventArgs eventArgs)
        {
            QueueMeshLoad(eventArgs.FullPath);
        }

        private void QueueMeshLoad(string path)
        {
            lock (pendingLock)
            {
                pendingMeshPath = path;
            }
        }

        private void IgnoreExistingMeshUntilNextWrite(string meshPath)
        {
            if (!minimumAcceptedMeshWriteTimeUtc.HasValue)
            {
                minimumAcceptedMeshWriteTimeUtc = DateTime.UtcNow;
            }

            if (!File.Exists(meshPath))
            {
                return;
            }

            lastPolledMeshWriteTimeUtc = File.GetLastWriteTimeUtc(meshPath);
        }

        private IEnumerator LoadMeshCoroutine(string meshPath)
        {
            yield return new WaitForSeconds(InitialLoadDelaySeconds);

            if (!ShouldAcceptMesh(meshPath))
            {
                activeLoadRoutine = null;
                yield break;
            }

            if (shadowMeshDeformer == null)
            {
                Debug.LogWarning("ShadowMeshFileLoader: ShadowMeshDeformer is not assigned; keeping the previous mesh.");
                stateManager?.OnShadowMeshLoadFailed(meshPath);
                activeLoadRoutine = null;
                yield break;
            }

            bool loadSucceeded = false;

            for (int attempt = 1; attempt <= MaxLoadAttempts; attempt++)
            {
                Mesh mesh = ObjParser.Parse(meshPath);
                if (mesh != null)
                {
                    int[] boundaryIndices = TryReadBoundaryIndices(meshPath);
                    shadowMeshDeformer.ReplaceMesh(mesh, boundaryIndices);
                    EnsureShadowMaterial();
                    stateManager?.OnShadowMeshLoaded(meshPath, mesh.vertexCount, boundaryIndices == null ? 0 : boundaryIndices.Length);
                    loadSucceeded = true;
                    break;
                }

                if (attempt < MaxLoadAttempts)
                {
                    yield return new WaitForSeconds(RetryDelaySeconds);
                }
            }

            if (!loadSucceeded)
            {
                stateManager?.OnShadowMeshLoadFailed(meshPath);
            }

            activeLoadRoutine = null;
        }

        private bool ShouldAcceptMesh(string meshPath)
        {
            if (!File.Exists(meshPath))
            {
                return false;
            }

            if (new FileInfo(meshPath).Length <= 0)
            {
                return false;
            }

            if (!minimumAcceptedMeshWriteTimeUtc.HasValue)
            {
                return true;
            }

            DateTime meshWriteTimeUtc = File.GetLastWriteTimeUtc(meshPath);
            return meshWriteTimeUtc >= minimumAcceptedMeshWriteTimeUtc.Value;
        }

        private void PollMeshFileIfNeeded()
        {
            if (Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + PollingIntervalSeconds;

            string meshPath = Path.Combine(GetWatchDirectoryAbsolute(), meshFileName);
            if (!File.Exists(meshPath) || !ShouldAcceptMesh(meshPath))
            {
                return;
            }

            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(meshPath);
            if (writeTimeUtc <= lastPolledMeshWriteTimeUtc)
            {
                return;
            }

            lastPolledMeshWriteTimeUtc = writeTimeUtc;
            QueueMeshLoad(meshPath);
        }

        private void EnsureShadowMaterial()
        {
            if (shadowMeshDeformer == null)
            {
                return;
            }

            MeshRenderer renderer = shadowMeshDeformer.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                Debug.LogWarning("ShadowMeshFileLoader: runtime unlit shader was not found.");
                return;
            }

            Material material = new Material(shader)
            {
                name = "RuntimeBlackShadowMaterial",
                hideFlags = HideFlags.DontSave
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.black);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.black);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0.0f);
            }

            renderer.sharedMaterial = material;
        }

        private int[] TryReadBoundaryIndices(string meshPath)
        {
            string directory = Path.GetDirectoryName(meshPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string metadataPath = Path.Combine(directory, metadataFileName);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(metadataPath);
                ShadowMetadata metadata = JsonUtility.FromJson<ShadowMetadata>(json);
                return metadata == null ? null : metadata.boundary_indices;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning($"ShadowMeshFileLoader: metadata parse failed for '{metadataPath}': {exception.Message}");
                return null;
            }
        }

        private string GetWatchDirectoryAbsolute()
        {
            if (!string.IsNullOrWhiteSpace(absoluteWatchDirectoryOverride) && Directory.Exists(absoluteWatchDirectoryOverride))
            {
                return Path.GetFullPath(absoluteWatchDirectoryOverride);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, relativeWatchDirectory));
        }

        private void DisposeWatcher()
        {
            if (watcher == null)
            {
                return;
            }

            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnMeshFileEvent;
            watcher.Created -= OnMeshFileEvent;
            watcher.Renamed -= OnMeshFileRenamed;
            watcher.Dispose();
            watcher = null;
        }

        [Serializable]
        private class ShadowMetadata
        {
            public int[] boundary_indices = Array.Empty<int>();
        }
    }
}
