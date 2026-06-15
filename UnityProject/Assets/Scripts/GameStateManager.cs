using UnityEngine;
using System;

namespace ShadowPrototype
{
    public class GameStateManager : MonoBehaviour
    {
        public enum PipelineState
        {
            Opening,
            Mission1,
            Mission2,
            Mission3,
            Mission4,
            Mission5,
            Ending
        }

        [SerializeField] private PipelineState currentState = PipelineState.Opening;
        [SerializeField] private string currentStateName = nameof(PipelineState.Opening);
        [SerializeField] private string keyword = string.Empty;
        public event Action<PipelineState> StateChanged;
        public event Action<string> KeywordChanged;
        public event Action<string, int, int> ShadowMeshLoaded;
        public event Action<string> ShadowMeshLoadFailed;

        public PipelineState CurrentState => currentState;
        public string CurrentStateName => currentStateName;
        public string Keyword => keyword;

        private void Awake()
        {
            ResetForCapture();
        }

        public void ResetForCapture()
        {
            currentState = PipelineState.Opening;
            currentStateName = currentState.ToString();
            keyword = string.Empty;
        }

        public void SetState(PipelineState nextState)
        {
            ApplyState(nextState);
        }

        public void OnOpeningStarted()
        {
            ApplyState(PipelineState.Opening);
        }

        public void OnShadowMeshLoaded(string path, int vertexCount, int boundaryCount)
        {
            Debug.Log($"GameStateManager: shadow mesh loaded. Vertices: {vertexCount}, Boundary: {boundaryCount}, Path: {path}");
            ShadowMeshLoaded?.Invoke(path, vertexCount, boundaryCount);
        }

        public void SetKeyword(string nextKeyword)
        {
            keyword = string.IsNullOrWhiteSpace(nextKeyword) ? string.Empty : nextKeyword.Trim();
            Debug.Log($"GameStateManager: keyword changed to '{keyword}'.");
            KeywordChanged?.Invoke(keyword);
        }

        public void OnMediaPipeTrackingStarted()
        {
            ApplyState(PipelineState.Mission1);
        }

        public void OnEndingStarted()
        {
            ApplyState(PipelineState.Ending);
        }

        public void OnShadowMeshLoadFailed(string path)
        {
            ApplyState(PipelineState.Ending);
            Debug.LogWarning($"GameStateManager: shadow mesh load failed; keeping the previous mesh. Path: {path}");
            ShadowMeshLoadFailed?.Invoke(path);
        }

        private void ApplyState(PipelineState nextState)
        {
            if (currentState == nextState)
            {
                return;
            }

            currentState = nextState;
            currentStateName = currentState.ToString();
            Debug.Log($"GameStateManager: state changed to {currentState}.");
            StateChanged?.Invoke(currentState);
        }
    }
}
