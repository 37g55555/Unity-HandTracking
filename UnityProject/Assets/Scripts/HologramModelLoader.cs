using System.IO;
using System.Text;
using GLTFast;
using ShadowPrototype;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HologramModelLoader : MonoBehaviour
{
    private const float RotationDurationSeconds = 15f;
    private const float ModelDisplayScale = 2.0f;
    private const float CameraFitPadding = 1.12f;
    private const float MinimumOrthographicSize = 1.0f;

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
        if (CenterAndNormalize(loadedModel, transform.position + modelWorldOffset, out Bounds normalizedBounds))
        {
            FitHologramCamerasToBounds(normalizedBounds);
        }

        Texture2D embeddedBaseColorTexture = LoadEmbeddedBaseColorTexture(path);
        MakeMaterialsUnlit(loadedModel, embeddedBaseColorTexture);
    }

    private static bool CenterAndNormalize(GameObject model, Vector3 targetWorldPosition, out Bounds bounds)
    {
        if (!TryGetRendererBounds(model, out bounds))
        {
            return false;
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0)
        {
            model.transform.localScale = Vector3.one * (ModelDisplayScale / maxSize);
        }

        if (!TryGetRendererBounds(model, out bounds))
        {
            return false;
        }

        Vector3 moveDelta = targetWorldPosition - bounds.center;
        model.transform.position += moveDelta;
        bounds.center = bounds.center + moveDelta;
        return true;
    }

    private void FitHologramCamerasToBounds(Bounds bounds)
    {
        foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
        {
            Camera[] cameras = rootObject.GetComponentsInChildren<Camera>(true);
            foreach (Camera sceneCamera in cameras)
            {
                if (IsHologramRenderCamera(sceneCamera))
                {
                    FitCameraToBounds(sceneCamera, bounds);
                }
            }
        }
    }

    private static bool IsHologramRenderCamera(Camera sceneCamera)
    {
        return sceneCamera != null &&
            sceneCamera.targetTexture != null &&
            (sceneCamera.name == "Cam_Front" ||
             sceneCamera.name == "Cam_Left" ||
             sceneCamera.name == "Cam_Right");
    }

    private static void FitCameraToBounds(Camera sceneCamera, Bounds bounds)
    {
        Vector3[] corners = GetBoundsCorners(bounds);
        float maxCameraX = 0f;
        float maxCameraY = 0f;
        float farthestCameraZ = 0f;

        for (int index = 0; index < corners.Length; index++)
        {
            Vector3 cameraPoint = sceneCamera.transform.InverseTransformPoint(corners[index]);
            maxCameraX = Mathf.Max(maxCameraX, Mathf.Abs(cameraPoint.x));
            maxCameraY = Mathf.Max(maxCameraY, Mathf.Abs(cameraPoint.y));
            farthestCameraZ = Mathf.Max(farthestCameraZ, cameraPoint.z);
        }

        float aspect = sceneCamera.aspect;
        if (sceneCamera.targetTexture != null && sceneCamera.targetTexture.height > 0)
        {
            aspect = sceneCamera.targetTexture.width / (float)sceneCamera.targetTexture.height;
        }

        float projectedHalfSize = Mathf.Max(maxCameraY, maxCameraX / Mathf.Max(0.01f, aspect));
        float rotationSafeHalfSize = bounds.extents.magnitude;
        float requiredHalfSize = Mathf.Max(projectedHalfSize, rotationSafeHalfSize);

        sceneCamera.orthographic = true;
        sceneCamera.orthographicSize = Mathf.Max(MinimumOrthographicSize, requiredHalfSize * CameraFitPadding);
        sceneCamera.nearClipPlane = 0.01f;
        sceneCamera.farClipPlane = Mathf.Max(sceneCamera.farClipPlane, farthestCameraZ + rotationSafeHalfSize + 10f);

        Debug.Log($"HologramModelLoader: fitted {sceneCamera.name} orthographicSize={sceneCamera.orthographicSize:0.00}.");
    }

    private static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z),
        };
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
            wrapMode = TextureWrapMode.Clamp,
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
        ConfigureDoubleSidedRendering(runtimeMaterial);
        return runtimeMaterial;
    }

    private static void ConfigureDoubleSidedRendering(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.doubleSidedGI = true;

        string[] cullProperties =
        {
            "_Cull",
            "_CullMode",
        };

        foreach (string propertyName in cullProperties)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, (float)UnityEngine.Rendering.CullMode.Off);
            }
        }
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
