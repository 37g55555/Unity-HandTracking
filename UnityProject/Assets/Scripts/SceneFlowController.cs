using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype
{
    public sealed class SceneFlowController : MonoBehaviour
    {
        [SerializeField] private bool keepThisRootAcrossSceneLoads;
        [SerializeField] private GameObject[] persistentObjects = Array.Empty<GameObject>();
        [SerializeField] private string initialSceneName = "Opening";
        [SerializeField] private bool loadInitialSceneOnStart;

        private Coroutine transitionRoutine;

        private void Awake()
        {
            if (keepThisRootAcrossSceneLoads || (persistentObjects != null && persistentObjects.Length > 0))
            {
                KeepRuntimeObjectsAcrossSceneLoads();
            }
        }

        private IEnumerator Start()
        {
            if (!loadInitialSceneOnStart || string.IsNullOrWhiteSpace(initialSceneName))
            {
                yield break;
            }

            yield return null;
            LoadScene(initialSceneName);
        }

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            transitionRoutine = StartCoroutine(LoadSceneRoutine(sceneName.Trim()));
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            Scene currentScene = SceneManager.GetActiveScene();
            if (currentScene.IsValid() && string.Equals(currentScene.name, sceneName, StringComparison.Ordinal))
            {
                transitionRoutine = null;
                yield break;
            }

            AsyncOperation loadOperation;
            try
            {
                loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"SceneFlowController: scene could not be loaded: {sceneName}. {exception.Message}");
                transitionRoutine = null;
                yield break;
            }

            if (loadOperation == null)
            {
                Debug.LogWarning($"SceneFlowController: scene could not be loaded: {sceneName}");
                transitionRoutine = null;
                yield break;
            }

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            Debug.Log($"SceneFlowController: loaded scene {sceneName}.");
            transitionRoutine = null;
        }

        private void KeepRuntimeObjectsAcrossSceneLoads()
        {
            var persistentRoots = new HashSet<GameObject>();
            if (keepThisRootAcrossSceneLoads)
            {
                persistentRoots.Add(transform.root.gameObject);
            }

            if (persistentObjects == null)
            {
                persistentObjects = Array.Empty<GameObject>();
            }

            foreach (GameObject persistentObject in persistentObjects)
            {
                if (persistentObject == null)
                {
                    continue;
                }

                persistentRoots.Add(persistentObject.transform.root.gameObject);
            }

            foreach (GameObject persistentRoot in persistentRoots)
            {
                DontDestroyOnLoad(persistentRoot);
            }
        }
    }
}
