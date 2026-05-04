#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowPrototype.Editor
{
    public static class ShadowPrototypeSceneCreator
    {
        [MenuItem("Tools/Shadow/Create Prototype Scene")]
        public static void CreatePrototypeScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrototypeBootstrap.EnsureSceneSetup();

            const string scenePath = "Assets/Scenes/ShadowPrototype.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            GameObject runtimeRoot = GameObject.Find("ShadowPrototypeRuntime");
            if (runtimeRoot != null)
            {
                Selection.activeGameObject = runtimeRoot;
            }

            Debug.Log($"Created and saved prototype scene at '{scenePath}'.");
        }
    }
}
#endif
