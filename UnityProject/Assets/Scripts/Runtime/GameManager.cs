using UnityEngine;
using System;

namespace ShadowPrototype
{
    public class GameManager : MonoBehaviour
    {
        public enum PrototypeState
        {
            Idle,
            CapturingShadow,
            MeshLoaded,
            HandTrackingActive,
            Error
        }

        [SerializeField] private PrototypeState currentState = PrototypeState.Idle;
        [SerializeField] private string lastLoadedMeshPath = string.Empty;
        [SerializeField] private int lastVertexCount;
        [SerializeField] private int lastBoundaryCount;

        public event Action<string, int, int> ShadowMeshLoaded;
        public event Action<string> ShadowMeshLoadFailed;

        public PrototypeState CurrentState => currentState;
        public string LastLoadedMeshPath => lastLoadedMeshPath;
        public int LastVertexCount => lastVertexCount;
        public int LastBoundaryCount => lastBoundaryCount;

        public void OnShadowCaptureStarted()
        {
            currentState = PrototypeState.CapturingShadow;
            Debug.Log("Shadow capture started.");
        }

        public void OnShadowMeshLoaded(string path, int vertexCount, int boundaryCount)
        {
            currentState = PrototypeState.MeshLoaded;
            lastLoadedMeshPath = path;
            lastVertexCount = vertexCount;
            lastBoundaryCount = boundaryCount;
            Debug.Log($"Shadow mesh loaded: {path} ({vertexCount} vertices, {boundaryCount} boundary indices).");
            ShadowMeshLoaded?.Invoke(path, vertexCount, boundaryCount);
        }

        public void OnHandTrackingStarted()
        {
            currentState = PrototypeState.HandTrackingActive;
            Debug.Log("Hand tracking started.");
        }

        public void OnShadowMeshLoadFailed(string path)
        {
            currentState = PrototypeState.Error;
            lastLoadedMeshPath = path;
            Debug.LogWarning($"Shadow mesh load failed and the previous mesh was kept: {path}");
            ShadowMeshLoadFailed?.Invoke(path);
        }
    }
}
