using UnityEngine;

namespace ShadowPrototype
{
    public class MediaPipeInteractionVisualizer : MonoBehaviour
    {
        [SerializeField] private MediaPipeMeshDeformationInput deformationInput;
        [SerializeField] private ShadowDeformer targetDeformer;
        [SerializeField] private Camera targetCamera;

        [Header("Marker Look")]
        [SerializeField] private float markerScaleMultiplier = 0.04f;
        [SerializeField] private float minimumMarkerSize = 0.025f;
        [SerializeField] private int ringSegments = 48;

        private Transform indexMarker;
        private Transform thumbMarker;
        private Transform middleMarker;
        private Transform grabMarker;
        private LineRenderer mainLine;
        private LineRenderer radiusRing;
        private TextMesh modeLabel;

        public void Configure(MediaPipeMeshDeformationInput input, ShadowDeformer deformer)
        {
            deformationInput = input;
            targetDeformer = deformer;
        }

        private void Awake()
        {
            ResolveDependencies();
            EnsureVisualObjects();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            ResolveDependencies();
            EnsureVisualObjects();

            if (deformationInput == null || targetDeformer == null || !targetDeformer.HasMesh || !deformationInput.HasProjectedPoints)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            float markerSize = ComputeMarkerSize();
            UpdateMarker(indexMarker, deformationInput.IndexWorldPoint, markerSize, new Color(1.0f, 0.76f, 0.15f));
            UpdateMarker(thumbMarker, deformationInput.ThumbWorldPoint, markerSize, new Color(0.18f, 0.89f, 1.0f));
            UpdateMarker(middleMarker, deformationInput.MiddleWorldPoint, markerSize, new Color(1.0f, 0.35f, 0.82f));
            UpdateMarker(grabMarker, deformationInput.GrabWorldPoint, markerSize * 0.85f, new Color(0.2f, 1.0f, 0.45f));

            mainLine.enabled = false;
            radiusRing.enabled = false;

            switch (deformationInput.CurrentMode)
            {
                case MediaPipeMeshDeformationInput.InteractionMode.Push:
                    DrawRing(deformationInput.IndexLocalPoint, deformationInput.PushRadiusLocal, new Color(1.0f, 0.45f, 0.15f), markerSize * 0.18f);
                    SetLabel("PUSH", deformationInput.IndexWorldPoint, new Color(1.0f, 0.45f, 0.15f), markerSize);
                    grabMarker.gameObject.SetActive(false);
                    break;

                case MediaPipeMeshDeformationInput.InteractionMode.Pull:
                    DrawLine(deformationInput.ThumbWorldPoint, deformationInput.IndexWorldPoint, new Color(0.23f, 1.0f, 0.5f), markerSize * 0.2f);
                    DrawRing(deformationInput.GrabLocalPoint, deformationInput.PullRadiusLocal, new Color(0.23f, 1.0f, 0.5f), markerSize * 0.18f);
                    SetLabel("PULL", deformationInput.GrabWorldPoint, new Color(0.23f, 1.0f, 0.5f), markerSize);
                    grabMarker.gameObject.SetActive(true);
                    break;

                case MediaPipeMeshDeformationInput.InteractionMode.Tear:
                    DrawLine(deformationInput.IndexWorldPoint, deformationInput.MiddleWorldPoint, new Color(1.0f, 0.18f, 0.18f), markerSize * 0.28f);
                    SetLabel("TEAR", (deformationInput.IndexWorldPoint + deformationInput.MiddleWorldPoint) * 0.5f, new Color(1.0f, 0.18f, 0.18f), markerSize);
                    grabMarker.gameObject.SetActive(false);
                    break;

                case MediaPipeMeshDeformationInput.InteractionMode.Hover:
                    SetLabel("TRACK", deformationInput.IndexWorldPoint, new Color(0.88f, 0.88f, 0.88f), markerSize);
                    grabMarker.gameObject.SetActive(false);
                    break;

                default:
                    modeLabel.gameObject.SetActive(false);
                    grabMarker.gameObject.SetActive(false);
                    break;
            }

            if (deformationInput.CurrentMode != MediaPipeMeshDeformationInput.InteractionMode.Pull)
            {
                grabMarker.gameObject.SetActive(false);
            }

            OrientLabelToCamera();
        }

        private void EnsureVisualObjects()
        {
            if (indexMarker == null)
            {
                indexMarker = CreateMarker("Index Marker", Color.yellow).transform;
            }

            if (thumbMarker == null)
            {
                thumbMarker = CreateMarker("Thumb Marker", Color.cyan).transform;
            }

            if (middleMarker == null)
            {
                middleMarker = CreateMarker("Middle Marker", Color.magenta).transform;
            }

            if (grabMarker == null)
            {
                grabMarker = CreateMarker("Grab Marker", Color.green).transform;
            }

            if (mainLine == null)
            {
                mainLine = CreateLineRenderer("Interaction Line");
            }

            if (radiusRing == null)
            {
                radiusRing = CreateLineRenderer("Radius Ring");
                radiusRing.loop = true;
            }

            if (modeLabel == null)
            {
                GameObject textObject = new GameObject("Interaction Label");
                textObject.transform.SetParent(transform, false);
                modeLabel = textObject.AddComponent<TextMesh>();
                modeLabel.text = string.Empty;
                modeLabel.anchor = TextAnchor.MiddleCenter;
                modeLabel.alignment = TextAlignment.Center;
                modeLabel.characterSize = 0.1f;
                modeLabel.fontSize = 32;
                modeLabel.color = Color.white;
            }
        }

        private void ResolveDependencies()
        {
            if (deformationInput == null)
            {
                deformationInput = UnityEngine.Object.FindAnyObjectByType<MediaPipeMeshDeformationInput>();
            }

            if (targetDeformer == null)
            {
                targetDeformer = UnityEngine.Object.FindAnyObjectByType<ShadowDeformer>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera == null)
                {
                    targetCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
                }
            }
        }

        private float ComputeMarkerSize()
        {
            Bounds bounds = targetDeformer.GetWorldBounds();
            float largestExtent = Mathf.Max(bounds.size.x, bounds.size.y);
            return Mathf.Max(minimumMarkerSize, largestExtent * markerScaleMultiplier);
        }

        private void UpdateMarker(Transform marker, Vector3 position, float markerSize, Color color)
        {
            marker.gameObject.SetActive(true);
            marker.position = position;
            marker.localScale = Vector3.one * markerSize;

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                {
                    renderer.sharedMaterial.SetColor("_BaseColor", color);
                }

                if (renderer.sharedMaterial.HasProperty("_Color"))
                {
                    renderer.sharedMaterial.SetColor("_Color", color);
                }
            }
        }

        private void DrawLine(Vector3 start, Vector3 end, Color color, float width)
        {
            mainLine.enabled = true;
            mainLine.positionCount = 2;
            mainLine.startColor = color;
            mainLine.endColor = color;
            mainLine.startWidth = width;
            mainLine.endWidth = width;
            mainLine.SetPosition(0, start);
            mainLine.SetPosition(1, end);
        }

        private void DrawRing(Vector2 localCenter, float localRadius, Color color, float width)
        {
            radiusRing.enabled = true;
            radiusRing.positionCount = ringSegments;
            radiusRing.startColor = color;
            radiusRing.endColor = color;
            radiusRing.startWidth = width;
            radiusRing.endWidth = width;

            for (int i = 0; i < ringSegments; i++)
            {
                float t = (float)i / ringSegments;
                float angle = t * Mathf.PI * 2.0f;
                Vector3 localPoint = new Vector3(
                    localCenter.x + (Mathf.Cos(angle) * localRadius),
                    localCenter.y + (Mathf.Sin(angle) * localRadius),
                    0.0f);
                Vector3 worldPoint = targetDeformer.transform.TransformPoint(localPoint);
                radiusRing.SetPosition(i, worldPoint);
            }
        }

        private void SetLabel(string text, Vector3 anchorWorldPoint, Color color, float markerSize)
        {
            modeLabel.gameObject.SetActive(true);
            modeLabel.text = text;
            modeLabel.color = color;
            modeLabel.characterSize = markerSize * 0.85f;
            modeLabel.transform.position = anchorWorldPoint + new Vector3(0.0f, markerSize * 2.1f, 0.0f);
        }

        private void OrientLabelToCamera()
        {
            if (modeLabel == null || targetCamera == null || !modeLabel.gameObject.activeSelf)
            {
                return;
            }

            modeLabel.transform.rotation = targetCamera.transform.rotation;
        }

        private void SetVisible(bool visible)
        {
            if (indexMarker != null) indexMarker.gameObject.SetActive(visible);
            if (thumbMarker != null) thumbMarker.gameObject.SetActive(visible);
            if (middleMarker != null) middleMarker.gameObject.SetActive(visible);
            if (grabMarker != null) grabMarker.gameObject.SetActive(false);
            if (mainLine != null) mainLine.enabled = false;
            if (radiusRing != null) radiusRing.enabled = false;
            if (modeLabel != null) modeLabel.gameObject.SetActive(false);
        }

        private GameObject CreateMarker(string name, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(transform, false);

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Material material = CreateUnlitMaterial(color);
            Renderer renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return marker;
        }

        private LineRenderer CreateLineRenderer(string name)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.material = CreateUnlitMaterial(Color.white);
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }
    }
}
