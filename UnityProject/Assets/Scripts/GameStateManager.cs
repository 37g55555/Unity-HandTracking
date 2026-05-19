using UnityEngine;
using System;

namespace ShadowPrototype
{
    public class GameStateManager : MonoBehaviour
    {
        public enum PipelineState
        {
            Idle,
            ShadowCapturing,
            MediaPipeTracking,
            MeshExtracting,
            Reconstructing3D,
            HologramOutput,
            Error
        }

        [SerializeField] private PipelineState currentState = PipelineState.Idle;
        [SerializeField] private string currentStateName = nameof(PipelineState.Idle);
        public event Action<string, int, int> ShadowMeshLoaded;
        public event Action<string> ShadowMeshLoadFailed;

        public PipelineState CurrentState => currentState;
        public string CurrentStateName => currentStateName;

        private void Awake()
        {
            ResetToIdle();
        }

        public void ResetToIdle()
        {
            currentState = PipelineState.Idle;
            currentStateName = currentState.ToString();
        }

        public void OnShadowCaptureStarted()
        {
            SetState(PipelineState.ShadowCapturing);
        }

        public void OnShadowMeshLoaded(string path, int vertexCount, int boundaryCount)
        {
            Debug.Log($"GameStateManager: shadow mesh loaded. Vertices: {vertexCount}, Boundary: {boundaryCount}, Path: {path}");
            ShadowMeshLoaded?.Invoke(path, vertexCount, boundaryCount);
        }

        public void OnMediaPipeTrackingStarted()
        {
            SetState(PipelineState.MediaPipeTracking);
        }

        public void OnMeshExtractionStarted()
        {
            SetState(PipelineState.MeshExtracting);
        }

        public void OnReconstructionStarted()
        {
            SetState(PipelineState.Reconstructing3D);
        }

        public void OnHologramOutputStarted()
        {
            SetState(PipelineState.HologramOutput);
        }

        public void OnShadowMeshLoadFailed(string path)
        {
            SetState(PipelineState.Error);
            Debug.LogWarning($"GameStateManager: shadow mesh load failed; keeping the previous mesh. Path: {path}");
            ShadowMeshLoadFailed?.Invoke(path);
        }

        private void SetState(PipelineState nextState)
        {
            if (currentState == nextState)
            {
                return;
            }

            currentState = nextState;
            currentStateName = currentState.ToString();
            Debug.Log($"GameStateManager: state changed to {currentState}.");
        }
    }
}
