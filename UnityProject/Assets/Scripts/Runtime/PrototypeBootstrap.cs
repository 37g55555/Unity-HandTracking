using System.IO;
using UnityEngine;

namespace ShadowPrototype
{
    public static class PrototypeBootstrap
    {
        private const string RuntimeRootName = "ShadowPrototypeRuntime";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            EnsureSceneSetup();
        }

        public static void EnsureSceneSetup()
        {
            GameObject runtimeRoot = GameObject.Find(RuntimeRootName);
            if (runtimeRoot == null)
            {
                runtimeRoot = new GameObject(RuntimeRootName);
            }

            Transform managers = EnsureChild(runtimeRoot.transform, "Managers");
            Transform shadowWorld = EnsureChild(runtimeRoot.transform, "Shadow World");
            Transform shadowMeshRoot = EnsureChild(shadowWorld, "ShadowMeshRoot");
            Transform shadowMeshVisual = EnsureChild(shadowMeshRoot, "ShadowMeshVisual");

            ShadowMeshRootController rootController = GetOrAddComponent<ShadowMeshRootController>(shadowMeshRoot.gameObject);
            ShadowDeformer shadowDeformer = GetOrAddComponent<ShadowDeformer>(shadowMeshVisual.gameObject);
            GameManager gameManager = GetOrAddComponent<GameManager>(managers.gameObject);
            LiveMeshLoader liveMeshLoader = GetOrAddComponent<LiveMeshLoader>(managers.gameObject);
            MockScaleInput mockScaleInput = GetOrAddComponent<MockScaleInput>(managers.gameObject);
            HandLandmarkUdpReceiver handReceiver = GetOrAddComponent<HandLandmarkUdpReceiver>(managers.gameObject);
            MediaPipeScaleInput mediaPipeScaleInput = GetOrAddComponent<MediaPipeScaleInput>(managers.gameObject);
            MediaPipeMeshDeformationInput mediaPipeMeshDeformationInput = GetOrAddComponent<MediaPipeMeshDeformationInput>(managers.gameObject);
            MediaPipeInteractionVisualizer mediaPipeInteractionVisualizer = GetOrAddComponent<MediaPipeInteractionVisualizer>(managers.gameObject);

            MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(shadowMeshVisual.gameObject);
            MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(shadowMeshVisual.gameObject);
            MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(shadowMeshVisual.gameObject);

            shadowDeformer.Configure(meshFilter, meshRenderer, meshCollider);
            liveMeshLoader.Configure(shadowDeformer, gameManager, GetCapOutputDirectoryAbsolute());
            mockScaleInput.Configure(rootController);
            mediaPipeScaleInput.Configure(rootController, handReceiver);
            mediaPipeMeshDeformationInput.Configure(shadowDeformer, handReceiver);
            mediaPipeInteractionVisualizer.Configure(mediaPipeMeshDeformationInput, shadowDeformer);

            shadowMeshRoot.localPosition = Vector3.zero;
            shadowMeshRoot.localRotation = Quaternion.identity;
            shadowMeshVisual.localPosition = Vector3.zero;
            shadowMeshVisual.localRotation = Quaternion.identity;

            if (meshRenderer.sharedMaterial == null)
            {
                meshRenderer.sharedMaterial = CreateShadowMaterial();
            }

            bool createdCamera;
            Camera camera = EnsureCamera(out createdCamera);
            ConfigureCamera(camera, createdCamera);
        }

        private static Transform EnsureChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            GameObject childObject = new GameObject(childName);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                component = target.AddComponent<T>();
            }

            return component;
        }

        private static Camera EnsureCamera(out bool createdCamera)
        {
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera != null)
            {
                createdCamera = false;
                return camera;
            }

            GameObject cameraObject = new GameObject("ProjectorCamera");
            camera = cameraObject.AddComponent<Camera>();
            createdCamera = true;
            return camera;
        }

        private static void ConfigureCamera(Camera camera, bool createdCamera)
        {
            if (!createdCamera)
            {
                return;
            }

            camera.orthographic = true;
            camera.orthographicSize = 2.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
            camera.transform.rotation = Quaternion.identity;
            camera.tag = "MainCamera";
        }

        private static Material CreateShadowMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = "RuntimeShadowMaterial";
            material.hideFlags = HideFlags.DontSave;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.black);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.black);
            }

            return material;
        }

        private static string GetCapOutputDirectoryAbsolute()
        {
            string userHome = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, "Downloads", "CAP_II", "output");
        }
    }
}
