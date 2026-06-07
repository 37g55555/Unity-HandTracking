using System.IO;
using System.Text;
using GLTFast;
using ShadowPrototype;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HologramModelLoader : MonoBehaviour
{
    private const float RotationDurationSeconds = 15f;

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
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return;
        }

        if (!File.Exists(modelPath))
        {
            Debug.LogError("HologramModelLoader: generated GLB file not found: " + modelPath);
            return;
        }

        await LoadGlb(modelPath);
    }

    private string ResolveModelPath()
    {
        SF3DGenerationClient generationClient = FindObjectOfType<SF3DGenerationClient>();
        if (generationClient == null)
        {
            Debug.LogError("HologramModelLoader: SF3DGenerationClient was not found.");
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(generationClient.LastGeneratedGlbPath))
        {
            Debug.LogError("HologramModelLoader: no generated GLB path is available.");
            return string.Empty;
        }

        Debug.Log("HologramModelLoader: loading generated GLB: " + generationClient.LastGeneratedGlbPath);
        return generationClient.LastGeneratedGlbPath;
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
        Texture2D embeddedBaseColorTexture = LoadEmbeddedBaseColorTexture(path);
        MakeMaterialsUnlit(loadedModel, embeddedBaseColorTexture);
    }

    private static void CenterAndNormalize(GameObject model, Vector3 targetWorldPosition)
    {
        if (!TryGetRendererBounds(model, out Bounds bounds))
        {
            return;
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0)
        {
            model.transform.localScale = Vector3.one * (1f / maxSize);
        }

        if (!TryGetRendererBounds(model, out bounds))
        {
            return;
        }

        model.transform.position += targetWorldPosition - bounds.center;
    }

    private static bool TryGetRendererBounds(GameObject model, out Bounds bounds)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        return true;
    }

    private static Texture2D LoadEmbeddedBaseColorTexture(string glbPath)
    {
        byte[] glbBytes = File.ReadAllBytes(glbPath);
        if (glbBytes.Length < 20 || Encoding.ASCII.GetString(glbBytes, 0, 4) != "glTF")
        {
            return null;
        }

        string json = null;
        int binStart = -1;
        int offset = 12;
        while (offset + 8 <= glbBytes.Length)
        {
            int chunkLength = unchecked((int)ReadUInt32(glbBytes, offset));
            uint chunkType = ReadUInt32(glbBytes, offset + 4);
            int chunkStart = offset + 8;
            if (chunkStart + chunkLength > glbBytes.Length)
            {
                break;
            }

            if (chunkType == 0x4E4F534A)
            {
                json = Encoding.UTF8.GetString(glbBytes, chunkStart, chunkLength).Trim('\0', ' ', '\t', '\r', '\n');
            }
            else if (chunkType == 0x004E4942)
            {
                binStart = chunkStart;
            }

            offset = chunkStart + chunkLength;
        }

        if (string.IsNullOrEmpty(json) || binStart < 0)
        {
            return null;
        }

        GltfJson gltfJson = JsonUtility.FromJson<GltfJson>(json);
        if (gltfJson?.materials == null || gltfJson.materials.Length == 0)
        {
            return null;
        }

        TextureInfo textureInfo = gltfJson.materials[0]?.pbrMetallicRoughness?.baseColorTexture;
        if (textureInfo == null ||
            gltfJson.textures == null ||
            textureInfo.index < 0 ||
            textureInfo.index >= gltfJson.textures.Length)
        {
            return null;
        }

        int imageIndex = gltfJson.textures[textureInfo.index].source;
        if (gltfJson.images == null || imageIndex < 0 || imageIndex >= gltfJson.images.Length)
        {
            return null;
        }

        int bufferViewIndex = gltfJson.images[imageIndex].bufferView;
        if (gltfJson.bufferViews == null || bufferViewIndex < 0 || bufferViewIndex >= gltfJson.bufferViews.Length)
        {
            return null;
        }

        BufferViewDef bufferView = gltfJson.bufferViews[bufferViewIndex];
        int imageStart = binStart + bufferView.byteOffset;
        if (bufferView.byteLength <= 0 || imageStart < 0 || imageStart + bufferView.byteLength > glbBytes.Length)
        {
            return null;
        }

        byte[] imageBytes = new byte[bufferView.byteLength];
        System.Array.Copy(glbBytes, imageStart, imageBytes, 0, bufferView.byteLength);

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "GLB_Embedded_BaseColor",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        if (!texture.LoadImage(imageBytes))
        {
            Destroy(texture);
            return null;
        }

        Debug.Log("HologramModelLoader: loaded embedded GLB baseColor texture.");
        return texture;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset] |
            (bytes[offset + 1] << 8) |
            (bytes[offset + 2] << 16) |
            (bytes[offset + 3] << 24));
    }

    private static void MakeMaterialsUnlit(GameObject model, Texture embeddedBaseColorTexture)
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
        int embeddedTextureCount = 0;
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                materials[index] = CreateRuntimeUnlitMaterial(
                    materials[index],
                    unlitShader,
                    embeddedBaseColorTexture,
                    out bool hasTexture,
                    out bool usedEmbeddedTexture);

                materialCount++;
                if (hasTexture)
                {
                    texturedMaterialCount++;
                }

                if (usedEmbeddedTexture)
                {
                    embeddedTextureCount++;
                }
            }

            renderer.sharedMaterials = materials;
        }

        Debug.Log($"HologramModelLoader: applied runtime unlit materials. Textured {texturedMaterialCount}/{materialCount}, embedded GLB textures {embeddedTextureCount}.");
    }

    private static Material CreateRuntimeUnlitMaterial(
        Material sourceMaterial,
        Shader unlitShader,
        Texture embeddedBaseColorTexture,
        out bool hasTexture,
        out bool usedEmbeddedTexture)
    {
        Texture baseMap = GetMaterialTexture(sourceMaterial);
        usedEmbeddedTexture = false;
        if (baseMap == null && embeddedBaseColorTexture != null)
        {
            baseMap = embeddedBaseColorTexture;
            usedEmbeddedTexture = true;
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

    [System.Serializable]
    private sealed class GltfJson
    {
        public MaterialDef[] materials;
        public TextureDef[] textures;
        public ImageDef[] images;
        public BufferViewDef[] bufferViews;
    }

    [System.Serializable]
    private sealed class MaterialDef
    {
        public PbrMetallicRoughnessDef pbrMetallicRoughness;
    }

    [System.Serializable]
    private sealed class PbrMetallicRoughnessDef
    {
        public TextureInfo baseColorTexture;
    }

    [System.Serializable]
    private sealed class TextureInfo
    {
        public int index = -1;
    }

    [System.Serializable]
    private sealed class TextureDef
    {
        public int source = -1;
    }

    [System.Serializable]
    private sealed class ImageDef
    {
        public int bufferView = -1;
    }

    [System.Serializable]
    private sealed class BufferViewDef
    {
        public int byteOffset;
        public int byteLength;
    }
}
