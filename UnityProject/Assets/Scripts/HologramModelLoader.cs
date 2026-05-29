using System.IO;
using GLTFast;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HologramModelLoader : MonoBehaviour
{
    private const float RotationDurationSeconds = 15f;
    private const string ModelFileName = "shadow_model.glb";

    [Header("Paths")]
    [SerializeField] private string inputDirectory = "D:/Unity-HandTracking/output/sf3d";

    private GameObject loadedModel;

    private void Start()
    {
        LoadModel();
    }

    private void Update()
    {
        if (loadedModel != null)
        {
            float rotationProgress = Time.time / RotationDurationSeconds;
            loadedModel.transform.rotation = Quaternion.AngleAxis(rotationProgress * 360f, Vector3.up);
        }
    }

    private async void LoadModel()
    {
        string modelPath = Path.Combine(inputDirectory, ModelFileName);
        if (!File.Exists(modelPath))
        {
            Debug.LogError("HologramModelLoader: GLB file not found: " + modelPath);
            return;
        }

        await LoadGlb(modelPath);
    }

    private async System.Threading.Tasks.Task LoadGlb(string path)
    {
        if (loadedModel != null)
        {
            Destroy(loadedModel);
        }

        var gltf = new GltfImport();
        bool success = await gltf.Load("file://" + path);
        if (!success)
        {
            Debug.LogError("HologramModelLoader: GLB load failed: " + path);
            return;
        }

        loadedModel = new GameObject("HologramObject");
        SceneManager.MoveGameObjectToScene(loadedModel, gameObject.scene);
        await gltf.InstantiateMainSceneAsync(loadedModel.transform);
        CenterAndNormalize(loadedModel);
        MakeMaterialsUnlit(loadedModel);
    }

    private static void CenterAndNormalize(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        model.transform.position = -bounds.center;

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0)
        {
            model.transform.localScale = Vector3.one * (1f / maxSize);
        }
    }

    private static void MakeMaterialsUnlit(GameObject model)
    {
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Unlit/Texture");
        }

        if (unlitShader == null)
        {
            Debug.LogWarning("HologramModelLoader: unlit shader was not found; keeping GLB materials.");
            return;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int index = 0; index < materials.Length; index++)
            {
                Texture baseMap = GetMaterialTexture(materials[index]);
                if (baseMap == null)
                {
                    continue;
                }

                materials[index].shader = unlitShader;
                SetMaterialTexture(materials[index], baseMap);
                SetMaterialColor(materials[index], Color.white);
            }
        }
    }

    private static Texture GetMaterialTexture(Material material)
    {
        string[] commonTextureProperties =
        {
            "_BaseMap",
            "_MainTex",
            "_BaseColorMap",
            "_BaseColorTexture",
            "_UnlitColorMap",
        };

        foreach (string propertyName in commonTextureProperties)
        {
            if (material.HasProperty(propertyName))
            {
                Texture texture = material.GetTexture(propertyName);
                if (texture != null)
                {
                    return texture;
                }
            }
        }

        Texture mainTexture = material.mainTexture;
        if (mainTexture != null)
        {
            return mainTexture;
        }

        foreach (string propertyName in material.GetTexturePropertyNames())
        {
            Texture texture = material.GetTexture(propertyName);
            if (texture != null)
            {
                return texture;
            }
        }

        return null;
    }

    private static void SetMaterialTexture(Material material, Texture texture)
    {
        if (texture == null)
        {
            return;
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }

        material.mainTexture = texture;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
