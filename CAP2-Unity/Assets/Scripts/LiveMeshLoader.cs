using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ShadowPrototype
{
    public class LiveMeshLoader : MonoBehaviour
    {
        [SerializeField] private string watchDirectoryRelative = "LiveMesh";
        [SerializeField] private string meshFilename = "shadow_mesh.obj";
        [SerializeField] private string metadataFilename = "shadow_metadata.json";
        [SerializeField] private float initialLoadDelaySeconds = 0.5f;
        [SerializeField] private float retryDelaySeconds = 0.25f;
        [SerializeField] private int maxLoadAttempts = 3;
        [SerializeField] private ShadowDeformer shadowDeformer;
        [SerializeField] private GameManager gameManager;
        //[SerializeField] private Camera targetCamera;
        //[SerializeField] private bool autoFrameTargetCameraOnLoad;
        [SerializeField] private string absoluteWatchDirectoryOverride = string.Empty;
        [SerializeField] private bool loadExistingMeshOnStart = false;

        private FileSystemWatcher watcher;
        private readonly object pendingLock = new object();
        private string pendingMeshPath;
        private Coroutine activeLoadRoutine;
        private DateTime? minimumAcceptedMeshWriteTimeUtc;

        public string WatchDirectoryAbsolute => GetWatchDirectoryAbsolute();

        public void Configure(ShadowDeformer deformer, GameManager manager, string watchDirectoryOverrideAbsolute = null)
        {
            shadowDeformer = deformer;
            gameManager = manager;

            if (!string.IsNullOrWhiteSpace(watchDirectoryOverrideAbsolute))
            {
                absoluteWatchDirectoryOverride = watchDirectoryOverrideAbsolute;
            }
        }

        public void SetLoadExistingMeshOnStart(bool shouldLoadExistingMeshOnStart)
        {
            loadExistingMeshOnStart = shouldLoadExistingMeshOnStart;
        }

        public void SetMinimumAcceptedMeshWriteTimeUtc(DateTime utcTimestamp)
        {
            minimumAcceptedMeshWriteTimeUtc = utcTimestamp;
        }

        public void ClearMinimumAcceptedMeshWriteTimeUtc()
        {
            minimumAcceptedMeshWriteTimeUtc = null;
        }

        private void Start()
        {
            ResolveDependencies();
            SetupWatcher();

            string existingMeshPath = Path.Combine(GetWatchDirectoryAbsolute(), meshFilename);
            if (loadExistingMeshOnStart && File.Exists(existingMeshPath))
            {
                QueueMeshLoad(existingMeshPath);
            }
        }

        private void Update()
        {
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
            Directory.CreateDirectory(watchDirectory);

            watcher = new FileSystemWatcher(watchDirectory, meshFilename);
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

        private IEnumerator LoadMeshCoroutine(string meshPath)
        {
            yield return new WaitForSeconds(initialLoadDelaySeconds);

            if (!ShouldAcceptMesh(meshPath))
            {
                activeLoadRoutine = null;
                yield break;
            }

            ResolveDependencies();
            if (shadowDeformer == null)
            {
                Debug.LogWarning("LiveMeshLoader could not find ShadowDeformer, so the previous mesh was kept.");
                gameManager?.OnShadowMeshLoadFailed(meshPath);
                activeLoadRoutine = null;
                yield break;
            }

            bool loadSucceeded = false;

            for (int attempt = 1; attempt <= maxLoadAttempts; attempt++)
            {
                Mesh mesh = ObjParser.Parse(meshPath);
                if (mesh != null)
                {
                    int[] boundaryIndices = TryReadBoundaryIndices(meshPath);
                    shadowDeformer.ReplaceMesh(mesh, boundaryIndices);
                    /*
                    if (autoFrameTargetCameraOnLoad)
                    {
                        FrameTargetCamera();
                    }
                    */
                    gameManager?.OnShadowMeshLoaded(meshPath, mesh.vertexCount, boundaryIndices == null ? 0 : boundaryIndices.Length);
                    loadSucceeded = true;
                    break;
                }

                if (attempt < maxLoadAttempts)
                {
                    yield return new WaitForSeconds(retryDelaySeconds);
                }
            }

            if (!loadSucceeded)
            {
                gameManager?.OnShadowMeshLoadFailed(meshPath);
            }

            activeLoadRoutine = null;
        }

        private bool ShouldAcceptMesh(string meshPath)
        {
            if (!minimumAcceptedMeshWriteTimeUtc.HasValue)
            {
                return true;
            }

            if (!File.Exists(meshPath))
            {
                return false;
            }

            DateTime meshWriteTimeUtc = File.GetLastWriteTimeUtc(meshPath);
            return meshWriteTimeUtc >= minimumAcceptedMeshWriteTimeUtc.Value;
        }

        private int[] TryReadBoundaryIndices(string meshPath)
        {
            string directory = Path.GetDirectoryName(meshPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string metadataPath = Path.Combine(directory, metadataFilename);
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
                Debug.LogWarning($"LiveMeshLoader could not parse metadata '{metadataPath}': {exception.Message}");
                return null;
            }
        }

        private void ResolveDependencies()
        {
            if (shadowDeformer == null)
            {
                shadowDeformer = UnityEngine.Object.FindAnyObjectByType<ShadowDeformer>();
            }

            if (gameManager == null)
            {
                gameManager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            }
            /*
            if (targetCamera == null)
            {
                targetCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            }
            */
        }

        private string GetWatchDirectoryAbsolute()
        {
            if (!string.IsNullOrWhiteSpace(absoluteWatchDirectoryOverride) && Directory.Exists(absoluteWatchDirectoryOverride))
            {
                return Path.GetFullPath(absoluteWatchDirectoryOverride);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, watchDirectoryRelative));
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
        /*
        private void FrameTargetCamera()
        {
            if (targetCamera == null || shadowDeformer == null || shadowDeformer.CurrentMesh == null)
            {
                return;
            }

            Bounds meshBounds = shadowDeformer.GetWorldBounds();
            float cameraAspect = Mathf.Max(targetCamera.aspect, 0.01f);
            float halfHeight = Mathf.Max(meshBounds.extents.y, meshBounds.extents.x / cameraAspect);
            float framingPadding = 1.35f;

            targetCamera.orthographic = true;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = Color.white;
            targetCamera.nearClipPlane = 0.01f;
            targetCamera.farClipPlane = 100.0f;
            targetCamera.transform.position = new Vector3(meshBounds.center.x, meshBounds.center.y, meshBounds.center.z - 5.0f);
            targetCamera.transform.rotation = Quaternion.identity;
            targetCamera.orthographicSize = Mathf.Max(halfHeight * framingPadding, 0.5f);
        }
        */
    }
}
