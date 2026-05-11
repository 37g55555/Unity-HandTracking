using UnityEngine;
using GLTFast;
using System.IO;
using System.Linq;

public class GlbLoader : MonoBehaviour
{
    [HideInInspector] public string inputDirectory;
    [HideInInspector] public GameObject loadedModel;

    public event System.Action OnLoadComplete;

    public async void StartLoad()
    {
        string latestFile = GetLatestGlbFile(inputDirectory);
        if (latestFile != null)
            await LoadGlb(latestFile);
        else
            Debug.LogError("폴더에 .glb 파일 없음: " + inputDirectory);
    }

    string GetLatestGlbFile(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Debug.LogError("폴더 없음: " + folderPath);
            return null;
        }
        return Directory.GetFiles(folderPath, "*.glb")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .FirstOrDefault();
    }

    async System.Threading.Tasks.Task LoadGlb(string path)
    {
        if (loadedModel != null)
            Destroy(loadedModel);

        var gltf = new GltfImport();
        bool success = await gltf.Load("file://" + path);

        if (success)
        {
            loadedModel = new GameObject("PlanetObject");
            await gltf.InstantiateMainSceneAsync(loadedModel.transform);
            CenterAndNormalize(loadedModel);
            Debug.Log("GLB 로드 완료: " + path);
            OnLoadComplete?.Invoke();
        }
        else
        {
            Debug.LogError("GLB 로드 실패: " + path);
        }
    }

    void CenterAndNormalize(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        model.transform.position = -bounds.center;

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0)
            model.transform.localScale = Vector3.one * (1f / maxSize);
    }
}