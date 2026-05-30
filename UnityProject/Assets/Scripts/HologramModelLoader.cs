using System.IO;
using GLTFast;
using ShadowPrototype;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HologramModelLoader : MonoBehaviour
{
    private const float RotationDurationSeconds = 15f;
    private const string ModelFileName = "shadow_model.glb";
    private const string TexturePreviewFileName = "last_texture.png";

    [Header("Paths")]
    [SerializeField] private string inputDirectory = "D:/Unity-HandTracking/output/sf3d";

    [Header("Placement")]
    [SerializeField] private Vector3 modelWorldOffset = Vector3.zero;

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
        string modelPath = ResolveModelPath();
        if (!File.Exists(modelPath))
        {
            Debug.LogError("HologramModelLoader: GLB file not found: " + modelPath);
            return;
        }

        await LoadGlb(modelPath);
    }

    private string ResolveModelPath()
    {
        SF3DGenerationClient generationClient = FindObjectOfType<SF3DGenerationClient>();
        if (generationClient != null &&
            !string.IsNullOrWhiteSpace(generationClient.LastGeneratedGlbPath) &&
            File.Exists(generationClient.LastGeneratedGlbPath))
        {
            Debug.Log("HologramModelLoader: loading generated GLB: " + generationClient.LastGeneratedGlbPath);
            return generationClient.LastGeneratedGlbPath;
        }

        string fallbackPath = Path.Combine(inputDirectory, ModelFileName);
        Debug.Log("HologramModelLoader: loading fallback GLB: " + fallbackPath);
        return fallbackPath;
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
        loadedModel.transform.SetParent(transform, false);
        await gltf.InstantiateMainSceneAsync(loadedModel.transform);
        CenterAndNormalize(loadedModel, transform.position + modelWorldOffset);
        Texture2D fallbackTexture = LoadFallbackTexture(path);
        MakeMaterialsUnlit(loadedModel, fallbackTexture);
    }

    private static void CenterAndNormalize(GameObject model, Vector3 targetWorldPosition)
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

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0)
        {
            model.transform.localScale = Vector3.one * (1f / maxSize);
        }

        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        model.transform.position += targetWorldPosition - bounds.center;
    }

    private Texture2D LoadFallbackTexture(string modelPath)
    {
        string texturePath = ResolveTexturePath(modelPath);
        if (string.IsNullOrEmpty(texturePath))
        {
            return null;
        }

        byte[] textureBytes = File.ReadAllBytes(texturePath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = Path.GetFileNameWithoutExtension(texturePath) + "_RuntimeTexture",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        if (!texture.LoadImage(textureBytes))
        {
            Debug.LogWarning("HologramModelLoader: fallback texture load failed: " + texturePath);
            Destroy(texture);
            return null;
        }

        Debug.Log("HologramModelLoader: fallback texture ready: " + texturePath);
        return texture;
    }

    private string ResolveTexturePath(string modelPath)
    {
        SF3DGenerationClient generationClient = FindObjectOfType<SF3DGenerationClient>();
        if (generationClient != null &&
            !string.IsNullOrWhiteSpace(generationClient.LastTexturePath) &&
            File.Exists(generationClient.LastTexturePath))
        {
            return generationClient.LastTexturePath;
        }

        string directory = Path.GetDirectoryName(modelPath);
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        string siblingTexturePath = Path.Combine(directory, TexturePreviewFileName);
        return File.Exists(siblingTexturePath) ? siblingTexturePath : string.Empty;
    }

    private static void MakeMaterialsUnlit(GameObject model, Texture fallbackTexture)
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
        int materialCount = 0;
        int texturedMaterialCount = 0;
        int fallbackTextureCount = 0;
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                materials[index] = CreateRuntimeUnlitMaterial(
                    materials[index],
                    unlitShader,
                    fallbackTexture,
                    out bool hasTexture,
                    out bool usedFallbackTexture);

                materialCount++;
                if (hasTexture)
                {
                    texturedMaterialCount++;
                }

                if (usedFallbackTexture)
                {
                    fallbackTextureCount++;
                }
            }

            renderer.sharedMaterials = materials;
        }

        Debug.Log($"HologramModelLoader: applied runtime unlit materials. Textured {texturedMaterialCount}/{materialCount}, fallback textures {fallbackTextureCount}.");
    }

    private static Material CreateRuntimeUnlitMaterial(
        Material sourceMaterial,
        Shader unlitShader,
        Texture fallbackTexture,
        out bool hasTexture,
        out bool usedFallbackTexture)
    {
        Texture baseMap = GetMaterialTexture(sourceMaterial);
        usedFallbackTexture = false;
        if (baseMap == null && fallbackTexture != null)
        {
            baseMap = fallbackTexture;
            usedFallbackTexture = true;
        }

        hasTexture = baseMap != null;
        Color baseColor = GetMaterialColor(sourceMaterial);
        Material runtimeMaterial = new Material(unlitShader)
        {
            name = $"{(sourceMaterial == null ? "GLB" : sourceMaterial.name)}_RuntimeUnlit",
            hideFlags = HideFlags.DontSave
        };

        SetMaterialTexture(runtimeMaterial, baseMap);
        SetMaterialColor(runtimeMaterial, baseColor);
        return runtimeMaterial;
    }

    private static Texture GetMaterialTexture(Material material)
    {
        if (material == null)
        {
            return null;
        }

        string[] commonTextureProperties =
        {
            "baseColorTexture",
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

    private static Color GetMaterialColor(Material material)
    {
        if (material == null)
        {
            return Color.white;
        }

        string[] commonColorProperties =
        {
            "baseColorFactor",
            "_BaseColor",
            "_Color",
            "_BaseColorFactor",
            "_UnlitColor",
        };

        foreach (string propertyName in commonColorProperties)
        {
            if (material.HasProperty(propertyName))
            {
                return material.GetColor(propertyName);
            }
        }

        return Color.white;
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
